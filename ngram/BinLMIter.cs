namespace ngram
{
    unsafe class BinLMIter
    {
        private readonly BinaryFile _binfile;
        private readonly int _currLevel;
        private readonly int _totalLevel;
        private bool _done;
        private long _currPos;
        private readonly bool _isInnerNode = true;
        private readonly long _startPos;
        private readonly long _endPos;
        private BinLMIter _subIter;//for iteration, we need [startpos, endpos, level]        
        public BinLMIter(BinaryFile binfile, int level)
        {
            _currLevel = level;
            _totalLevel = level;
            _binfile = binfile;
            _startPos = 0;
            _endPos = binfile.PAccCount[1] - 1;
        }

        BinLMIter(BinaryFile binfile, int totalLevel, int currLevel, long startPos, long endPos, bool isInnerNode = true)
        {
            _totalLevel = totalLevel;
            _currLevel = currLevel;
            _binfile = binfile;
            _startPos = startPos;
            _endPos = endPos;
            _currPos = _startPos;
            _isInnerNode = isInnerNode;
        }

        public long MoveNextX(ref int[] xkeys, int index = 0)
        {
            long currPos = 0;
            if (_currLevel == 0)
            {
                if (_done)
                    return -1;
                _done = true;
            }
            else if (_currLevel == 1)
            {
                if (_currPos == _endPos) //1-gram
                    return -1;
                if (_isInnerNode)
                {
                    InnerNode currNode = ((InnerNode*)_binfile.InnerPtr)[_currPos];
                    xkeys[index] = currNode.Index;
                    xkeys[index + 1] = currNode.Count;
                }
                else
                {
                    LeafNode currNode =
                        ((LeafNode*)_binfile.FinalPtr)[_currPos - _binfile.PAccCount[_binfile.PAccCount.Count - 1]];
                    xkeys[index] = currNode.Index;
                    xkeys[index + 1] = currNode.Count;
                }
                currPos = _currPos;
                _currPos++;
            }
            else
            {
                while (true)
                {
                    if (_subIter == null)
                    {
                        if (_currPos == _endPos || _startPos == _endPos)
                            return -1;
                        InnerNode currNode = ((InnerNode*)_binfile.InnerPtr)[_currPos];
                        xkeys[index] = currNode.Index;
                        long currChild = _binfile.PAccCount[_totalLevel - _currLevel + 1] + currNode.Child;
                        InnerNode nextNode = ((InnerNode*)_binfile.InnerPtr)[_currPos + 1];
                        long nextChild = _binfile.PAccCount[_totalLevel - _currLevel + 1] + nextNode.Child;
                        _currPos++;
                        _subIter = new BinLMIter(_binfile, _totalLevel, _currLevel - 1, currChild, nextChild,
                                                 !(_currLevel == 2 && _totalLevel == _binfile.PAccCount.Count));
                    }
                    currPos = _subIter.MoveNextX(ref xkeys, index + 1);
                    if (currPos > 0)
                        return currPos;
                    _subIter = null;
                }
            }
            return currPos;
        }
    }
}