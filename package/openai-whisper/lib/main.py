from src.model import WhisperModelCT2
from src.transcribe import transcribe_samples

import torch
import sys
import json
import traceback
from jfk import jfk

def run_request(request):
    body = request["body"]
    global model
    match request["requestType"]:
        case "isCudaAvailable":
            return torch.cuda.is_available()
        case "initModel":
            use_cuda = body["useCuda"]
            model = WhisperModelCT2(
                model_path=body["modelPath"],
                device="cuda" if use_cuda else "cpu",
                compute_type="float16" if use_cuda else "float32",
                cpu_threads=body["cpuThreads"],
                num_workers=body["workers"],
            )
            return "Success"
        case "transcribe":
            batchSize = len(body["samplesBatch"])
            return transcribe_samples(
                model,
                samples_batch=list(map(bytes, body["samplesBatch"])),
                lang_codes=[body["language"]],
                batch_size=batchSize,
            )

def read_null_terminated_utf_string(input_stream):
    buffer = bytearray()
    while True:
        byte = input_stream.read(1)
        if not byte:
            raise EOFError(f"Unexpected end of input stream. byte: {byte}")
        if byte == b"\x00":
            break
        buffer.extend(byte)
    return buffer.decode("utf-8-sig")

#if __name__ == "__main__":
#    global model
#    stdin = sys.stdin.buffer
#    stdout = sys.stdout.buffer
#    def write_response(response):
#        stdout.write(json.dumps(response).encode("utf-8"))
#        stdout.write(b"\x00")
#        stdout.flush()
#    while True:
#        try:
#            input = json.loads(read_null_terminated_utf_string(stdin))
#            response = run_request(input)
#            write_response({ "response": response })
#        except Exception:
#            write_response({ "exception": traceback.format_exc() })
#            break
if __name__ == "__main__":
    global model
    #stdin = sys.stdin.buffer
    #stdout = sys.stdout.buffer
    #def write_response(response):
    #    stdout.write(json.dumps(response).encode("utf-8"))
    #    stdout.write(b"\x00")
    #    stdout.flush()
    response = run_request({
        "requestType": "initModel",
        "body": {
            "modelPath": "../../../model/whisper-base",
            "useCuda": False,
            "cpuThreads": 4,
            "workers": 1
        }
    })
    print(response)
    response = run_request({
        "requestType": "transcribe",
        "body": {
            "samplesBatch": [jfk],
            "language": "en"
        }
    })
    print(response)
        #write_response({ "response": response })
        #write_response({ "exception": traceback.format_exc() })
        #break