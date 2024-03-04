# WhisperS2T does not support in-memory transcriptions.
# This is taken from the WhisperS2T source code and modified to support it.
# WhisperS2T is licensed under MIT, which you can view here: https://github.com/shashikg/WhisperS2T/blob/main/LICENSE
# Credits: https://github.com/shashikg/WhisperS2T

from src.loader import WhisperDataLoader
from itertools import chain

import torch
import numpy as np

# Only 16khz is supported by default.
sampling_rate = 16000

def fix_batch_param(param, default_value, N):
    if param is None:
        param = N*[default_value]
    elif type(param) == type(default_value):
        param = N*[param]
    elif len(param) != N:
        param = N*[param[0]]
    return param

def to_audio_signal(samples: bytes):
    return np.frombuffer(samples, np.int16).flatten().astype(np.float32) / 32768.0

def load_data(loader, audio_signals, lang_codes, tasks, initial_prompts, batch_size):
    segmented_audio_signal = []
    for task_id, (audio_signal, lang, task, initial_prompt) in enumerate(zip(audio_signals, lang_codes, tasks, initial_prompts)):
        new_segmented_audio_signal = loader.get_segmented_audio_signal(audio_signal, task_id, lang, task, initial_prompt)
        segmented_audio_signal = segmented_audio_signal + new_segmented_audio_signal
        while len(segmented_audio_signal) > batch_size:
            batch = segmented_audio_signal[:batch_size]
            segmented_audio_signal = segmented_audio_signal[batch_size:]
            signal_batch, prompt_batch, seq_len, seg_metadata = loader.data_collate_fn(batch)
            yield signal_batch, prompt_batch, seq_len, seg_metadata
    signal_batch, prompt_batch, seq_len, seg_metadata = loader.data_collate_fn(segmented_audio_signal)
    yield signal_batch, prompt_batch, seq_len, seg_metadata

@torch.no_grad()
def transcribe_samples(model, samples_batch: list[bytes], lang_codes: list[str], batch_size):
    audio_signals = list(map(to_audio_signal, samples_batch))
    lang_codes = fix_batch_param(lang_codes, "en", len(audio_signals))
    tasks = fix_batch_param(None, "transcribe", len(audio_signals))
    initial_prompts = fix_batch_param(None, None, len(audio_signals))
    loader = WhisperDataLoader(
        model.device,
        model.tokenizer,
        model.speech_segmenter, 
        dta_padding=model.dta_padding,
        without_timestamps=model.without_timestamps, 
        max_speech_len=model.max_speech_len, 
        max_initial_prompt_len=model.max_initial_prompt_len, 
        use_dynamic_time_axis=model.use_dynamic_time_axis,
        merge_chunks=model.merge_chunks
    )
    responses = [[] for _ in audio_signals]
    for signals, prompts, seq_len, seg_metadata in load_data(loader, audio_signals, lang_codes, tasks, initial_prompts, batch_size=batch_size):
        mels, seq_len = model.preprocessor(signals, seq_len)
        res = model.generate_segment_batched(mels.to(model.device), prompts, seq_len, seg_metadata)

        for res_idx, _seg_metadata in enumerate(seg_metadata):
            result = {
                **res[res_idx],
                "startTime": round(_seg_metadata["start_time"], 3),
                "endTime": round(_seg_metadata["end_time"], 3)
            }
            result["avgLogProb"] = result.pop("avg_logprob")
            result["noSpeechProb"] = result.pop("no_speech_prob")
            responses[_seg_metadata["file_id"]].append(result)
    return list(chain(*responses))