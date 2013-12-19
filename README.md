frac-ngram
================

A language model tool based on suffix array, which can train language model under both fractional(for Modified KN) and integer count(for Witten-Bell, Good-Turing, KN and Modified KN).

Usage:
   frac-ngram 
        -smoothing [mkn|gt|wb|kn] 
        -interpolate [true|false] 
        -text train-text-file 
        -unk -useCutoff cutoff-number 
        -order order-number 
        -vocab dump-vocab-file 
        -revserse language model is 
        -dumpBinLM
        -dumpArpaLM
        -gtmin
        -gtmax
        -applyFracMKNSmoothing 

If you have any question, please contact yinggong.zhao@gmail.com.
