# WhisperS2T does not support in-memory transcriptions.
# This is taken from the WhisperS2T source code and modified to support it.
# WhisperS2T is licensed under MIT, which you can view here: https://github.com/shashikg/WhisperS2T/blob/main/LICENSE
# Credits: https://github.com/shashikg/WhisperS2T

from whisper_s2t.configs import *
from src.audio import pad_or_trim

import torch
import numpy as np

def stitch_speech_segments(start_ends, max_len=27.0, max_silent_region=None):
    speech_duration = [end - start for start, end in start_ends]
    stitched_speech_segments = []
    curr_seg = [0]
    curr_dur = speech_duration[0]
    idx = 1
    while idx < len(start_ends):
        if curr_dur + speech_duration[idx] > max_len:
            stitched_speech_segments.append([start_ends[_] for _ in curr_seg])
            curr_seg = [idx]
            curr_dur = speech_duration[idx]
        else:
            curr_dur += speech_duration[idx]
            curr_seg.append(idx)
            
        idx += 1
        
    stitched_speech_segments.append([start_ends[_] for _ in curr_seg])
    
    if max_silent_region is None:
        return stitched_speech_segments
    
    stitched_speech_segments_joined = []
    for segs in stitched_speech_segments:
        _segs = []
        curr_seg_start_time, curr_seg_end_time = segs[0]
        for i in range(1, len(segs)):
            if (segs[i][0] - curr_seg_end_time) >= max_silent_region:
                _segs.append((curr_seg_start_time, curr_seg_end_time))
                curr_seg_start_time = segs[i][0]

            curr_seg_end_time = segs[i][1]

        _segs.append((curr_seg_start_time, curr_seg_end_time))
        
        stitched_speech_segments_joined.append(_segs)
        
    
    return stitched_speech_segments_joined


class BasicSegmenter:
    def __init__(self, max_seg_len=29.0, sampling_rate=16000):
        self.max_seg_len = max_seg_len
        self.sampling_rate = sampling_rate

    def __call__(self, audio_signal):
        audio_duration = len(audio_signal)/self.sampling_rate
        start_ends = []
        for i in range(0, int(audio_duration), int(self.max_seg_len)):
            start_ends.append([i, i + self.max_seg_len])
        start_ends[-1][1] = min(audio_duration, start_ends[-1][1]) # fix edge
        return start_ends, audio_signal


class WhisperDataset(torch.utils.data.Dataset):
    def __init__(self, audio_signals, lang_codes, tasks, initial_prompts, tokenizer, max_initial_prompt_len, 
                 device="cuda", 
                 dta_padding=48000,
                 without_timestamps=True,
                 use_dynamic_time_axis=False):
        
        self.audio_signals = audio_signals
        self.lang_codes = lang_codes
        self.tasks = tasks
        self.initial_prompts = initial_prompts
        self.tokenizer = tokenizer
        self.device = device
        self.dta_padding = dta_padding
        self.without_timestamps = without_timestamps
        self.use_dynamic_time_axis = use_dynamic_time_axis
        self.max_initial_prompt_len = max_initial_prompt_len
        self.get_audio_signal = self._get_audio_signal_from_array
        
    def _get_audio_signal_from_array(self, item):
        return self.audio_signals[item]

    def __len__(self):
        return len(self.audio_signals)

    def __getitem__(self, item):
        audio = self.get_audio_signal(item)
        seq_len = audio.shape[-1]
        
        if self.initial_prompts[item]:
            initial_prompt = " " + self.initial_prompts[item].strip()
            initial_prompt_tokens = self.tokenizer.encode(initial_prompt)[-self.max_initial_prompt_len:]
        else:
            initial_prompt_tokens = []
        
        prompt = self.tokenizer.sot_sequence(task=self.tasks[item], lang=self.lang_codes[item])
        
        if self.without_timestamps:
            prompt = prompt + [self.tokenizer.no_timestamps]
            
        return audio, prompt, initial_prompt_tokens, seq_len


class WhisperDataLoader:
    def __init__(self, device, tokenizer, speech_segmenter, 
                 dta_padding=3.0, 
                 without_timestamps=True, 
                 max_speech_len=29.0, 
                 max_initial_prompt_len=223,
                 merge_chunks=True,
                 use_dynamic_time_axis=False):
        
        self.device = device
        self.tokenizer = tokenizer
        self.speech_segmenter = speech_segmenter
        self.basic_segmenter = BasicSegmenter(max_seg_len=max_speech_len)
        self.dta_padding = int(dta_padding*SAMPLE_RATE)
        self.without_timestamps = without_timestamps
        self.max_speech_len = max_speech_len
        self.max_initial_prompt_len = max_initial_prompt_len
        self.use_dynamic_time_axis = use_dynamic_time_axis
        self.merge_chunks = merge_chunks
        
    def data_collate_fn(self, batch):
        if self.use_dynamic_time_axis:
            max_len = min(max([_[3] for _ in batch]) + self.dta_padding, N_SAMPLES)
        else:
            max_len = N_SAMPLES

        signal_batch = torch.stack([torch.from_numpy(pad_or_trim(_[0], length=max_len)).to(self.device) for _ in batch])
        seq_len = torch.tensor([_[3] for _ in batch]).to(self.device)

        prompt_batch = []
        initial_prompt_max_len = max([len(_[2]) for _ in batch])
        if initial_prompt_max_len:
            for _ in batch: prompt_batch.append([self.tokenizer.sot_prev] + (initial_prompt_max_len-len(_[2]))*[self.tokenizer.silent_token] + _[2] + _[1])
        else:
            for _ in batch: prompt_batch.append(_[1])

        if len(batch[0]) == 5:
            seg_metadata = [_[4] for _ in batch]
            return signal_batch, prompt_batch, seq_len, seg_metadata
        else:
            return signal_batch, prompt_batch, seq_len
    
    def get_segmented_audio_signal(self, start_ends, audio_signal, file_id, lang, task, initial_prompt, sr=16000):

        if initial_prompt:
            initial_prompt = " " + initial_prompt.strip()
            initial_prompt_tokens = self.tokenizer.encode(initial_prompt)[-self.max_initial_prompt_len:]
        else:
            initial_prompt_tokens = []

        prompt = self.tokenizer.sot_sequence(task=task, lang=lang)
        
        if self.without_timestamps:
            prompt.append(self.tokenizer.no_timestamps)
        else:
            prompt.append(self.tokenizer.timestamp_begin)

        segmented_audio_signal = []

        if self.merge_chunks:
            stitched_speech_segments = stitch_speech_segments(start_ends, max_len=self.max_speech_len)
            for stitched_seg in stitched_speech_segments:
                audio = []
                for st, et in stitched_seg:
                    audio.append(audio_signal[int(st*sr):int(et*sr)])

                audio = np.concatenate(audio)
                seq_len = audio.shape[-1]
                seg_metadata = {
                    'file_id': file_id, 
                    'start_time': stitched_seg[0][0], 
                    'end_time': stitched_seg[-1][1], 
                    'stitched_seg': stitched_seg,
                    'lang_code': lang
                }
                segmented_audio_signal.append((audio, prompt, initial_prompt_tokens, seq_len, seg_metadata))
        else:
            for st, et in start_ends:
                audio = audio_signal[int(st*sr):int(et*sr)]
                seq_len = audio.shape[-1]
                segmented_audio_signal.append((audio, prompt, initial_prompt_tokens, seq_len, {'file_id': file_id, 'start_time': st, 'end_time': et}))

        return segmented_audio_signal
    
    def get_data_loader_with_vad(self, audio_signals, lang_codes, tasks, initial_prompts, batch_size=16):
        segmented_audio_signal = []
        for file_id, (audio_signal, lang, task, initial_prompt) in enumerate(zip(audio_signals, lang_codes, tasks, initial_prompts)):
            start_ends, audio_signal = self.speech_segmenter(audio_signal=audio_signal)
            new_segmented_audio_signal = self.get_segmented_audio_signal(start_ends, audio_signal, file_id, lang, task, initial_prompt)
            segmented_audio_signal = segmented_audio_signal + new_segmented_audio_signal
            while len(segmented_audio_signal) > batch_size:
                batch = segmented_audio_signal[:batch_size]
                segmented_audio_signal = segmented_audio_signal[batch_size:]
                signal_batch, prompt_batch, seq_len, seg_metadata = self.data_collate_fn(batch)
                yield signal_batch, prompt_batch, seq_len, seg_metadata
        signal_batch, prompt_batch, seq_len, seg_metadata = self.data_collate_fn(segmented_audio_signal)
        yield signal_batch, prompt_batch, seq_len, seg_metadata
    
    def get_data_loader(self, audio_signals, lang_codes, tasks, initial_prompts, batch_size=16):
        segmented_audio_signal = []
        for file_id, (audio_signal, lang, task, initial_prompt) in enumerate(zip(audio_signals, lang_codes, tasks, initial_prompts)):
            start_ends, audio_signal = self.basic_segmenter(audio_signal=audio_signal)
            new_segmented_audio_signal = self.get_segmented_audio_signal(start_ends, audio_signal, file_id, lang, task, initial_prompt)
            segmented_audio_signal = segmented_audio_signal + new_segmented_audio_signal
            while len(segmented_audio_signal) > batch_size:
                batch = segmented_audio_signal[:batch_size]
                segmented_audio_signal = segmented_audio_signal[batch_size:]
                signal_batch, prompt_batch, seq_len, seg_metadata = self.data_collate_fn(batch)
                yield signal_batch, prompt_batch, seq_len, seg_metadata
        signal_batch, prompt_batch, seq_len, seg_metadata = self.data_collate_fn(segmented_audio_signal)
        yield signal_batch, prompt_batch, seq_len, seg_metadata
    
    def __call__(self, audio_signals, lang_codes, tasks, initial_prompts, batch_size=16, use_vad=True):
        if use_vad:
            return self.get_data_loader_with_vad(audio_signals, lang_codes, tasks, initial_prompts, batch_size=batch_size)
        else:
            return self.get_data_loader(audio_signals, lang_codes, tasks, initial_prompts, batch_size=batch_size)