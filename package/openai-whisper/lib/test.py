from src.model import WhisperModelCT2
from src.transcribe import transcribe_samples

import unittest
import wave

model_path = "../../../model/whisper-base"

if __name__ == "__main__":
    unittest.main()

class Test(unittest.TestCase):
    def test_transcribe(self):
        with wave.open("../jfk.wav", "rb") as wave_file:
            samples = wave_file.readframes(int(wave_file.getnframes()))
            model = WhisperModelCT2(
                model_path=model_path,
                device="cpu",
                compute_type="float32",
            )
            response = transcribe_samples(model, samples_batch=[samples], lang_codes=["en"], batch_size=1)
            expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
            self.assertEqual(expected, response[0]["text"])