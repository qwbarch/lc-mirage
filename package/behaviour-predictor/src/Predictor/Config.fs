module Predictor.Config

type Config = 
    {   VOICE_BUFFER: int
        MIL_PER_OBS: int
        AFK_MILLIS: int
        FILE_SPLIT_SIZE: int
        MIMIC_POLICY_UPDATE_REPEAT: int
        SCORE_TALK_BIAS: float
        SCORE_EXPLORATION: float
    }
let mutable config = 
    {   VOICE_BUFFER = 5000
        MIL_PER_OBS = 300
        AFK_MILLIS = 10000
        FILE_SPLIT_SIZE = 300
        MIMIC_POLICY_UPDATE_REPEAT = 500
        SCORE_TALK_BIAS = 0.0
        SCORE_EXPLORATION = 5
    }