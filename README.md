frac-ngram
================

A language model tool based on suffix array, which can train LM under both fractional(for Modified KN) and integer count(for Witten-Bell, Good-Turing, KN and Modified KN).

Usage:
   frac-ngram 
        -smoothing [mkn|gt|wb|kn] 
        
        -interpolate [true|false] 
        
        -text train-text-file 
        
        -unk -useCutoff cutoff-number 
        
        -order order-number 
        
        -vocab dump-vocab-file 
        
        -revserse language model is 
        
        -dumpBinLM bin-file
        
        -dumpArpaLM arpa-file
        
        -gtmin min-count for cutoff
        
        -gtmax
        
        -applyFracMKNSmoothing 
        
        -weight weight-file for training

If you have any question, please contact yinggong.zhao@gmail.com.
