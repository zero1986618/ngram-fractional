using System;
using System.Collections.Generic;
using System.IO;

namespace ngram
{
    unsafe class RecurseGenBin
    {
        public RecurseGenBin(int order)
        {
            _order = order;
            if (applyFracMKNSmoothing)
                appendValues = 3;
        }
        bool applyFracMKNSmoothing = LMConfig.GetOption("applyFracMKNSmoothing", false);
        private BinaryReader[] _binaryReaders;
        private readonly int _order;
        public long[] Ngramcounts;
        private string _lmbin;
        private long[] _prevFillLocation;
        private long[] _currFillLocation;
        private BinaryFile _binaryFile;
        public BinaryFile BinaryFile { get { return _binaryFile; } }
        public byte* InnerPtr
        {
            get { return _binaryFile.InnerPtr; }
        }
        public byte* FinalPtr
        {
            get { return _binaryFile.FinalPtr; }
        }
        public List<long> PAccCount
        {
            get { return _binaryFile.PAccCount; }
        }
        void ReadBin()
        {
            string smoothing = LMConfig.GetOption("smoothing");        
            _binaryReaders = new BinaryReader[_order];
            for (int i = 0; i < _order; i++)
                _binaryReaders[i] = new BinaryReader(new FileStream(smoothing + "." + (i + 1) + "gram.bin", FileMode.Open));
        }

        private int[][] _prevNGrams;
        private int[][] _currNGrams;
        private int appendValues = 2;
        public void GenBin(string lmbin)
        {
            _prevNGrams = new int[_order][];
            _currNGrams = new int[_order][];
            for (int i = 0; i < _order; i++)
            {
                _currNGrams[i] = new int[i + appendValues];
                _prevNGrams[i] = new int[i + appendValues];
            }
            _prevNGrams[0] = new int[appendValues];
            for (int i = 0; i < _prevNGrams[0].Length; i++)
                _prevNGrams[0][i] = -1;
            _lmbin = lmbin;
            Ngramcounts = new long[_order];
            _currFillLocation = new long[_order + 1]; //current filled location
            _prevFillLocation = new long[_order + 1]; //current filled location
            ReadBin();
            for (int i = 0; i < _order; i++)
                Ngramcounts[i] = (_binaryReaders[i].BaseStream.Length/(sizeof (int)*(i + appendValues)));
            _binaryFile = new BinaryFile(_lmbin, Ngramcounts);
            _finalAppends = new bool[_order];
            for (int i = 0; i < _finalAppends.Length; i++)
                _finalAppends[i] = true;
            RecurseRead(_order);
            foreach (BinaryReader t in _binaryReaders)
                t.Close();
            for (int i = 0; i < _order - 1; i++)
            {
                InnerNode innerNode = new InnerNode
                {
                    Index = 0x00ff00ff,
                    Count = 0x00ee00ee,
                    Child = Ngramcounts[i + 1]
                };
                ((InnerNode*)InnerPtr)[PAccCount[i + 1] - 1] = innerNode;
            }
            {
                LeafNode leafNode = new LeafNode { Index = 0x00ff00ff, Count = 0x00ee00ee };
                ((LeafNode*)FinalPtr)[Ngramcounts[Ngramcounts.Length - 1]] = leafNode;
            }
        }

        private int recursive=0;
        private bool[] _finalAppends;
        public void RecurseRead(int level)
        {
            recursive++;
            while (_finalAppends[level - 1])
            {
                if (_binaryReaders[level - 1].BaseStream.Position >= _binaryReaders[level - 1].BaseStream.Length)
                {
                    _finalAppends[level - 1] = false;
                    for (int i = 0; i < level; i++)
                        _currNGrams[level - 1][i] = int.MaxValue;
                }
                else
                    for (int i = 0; i < level + appendValues - 1; i++)
                        _currNGrams[level - 1][i] = _binaryReaders[level - 1].ReadInt32();
                int gramMatchIndex = int.MinValue;
                for (int i = 0; i < level; i++)
                    if (_currNGrams[level - 1][i] == _prevNGrams[level - 1][i])
                        gramMatchIndex = i + 1;
                    else
                        break;
                if (gramMatchIndex == level)
                    throw new Exception("error! duplicated ngrams");
                if (level == _order)
                {
                    LeafNode leafNode;
                    leafNode.Prob = 0;
                    if (applyFracMKNSmoothing)
                        leafNode.Prob = Util.Int2Float(_currNGrams[level - 1][_currNGrams[level - 1].Length - 1]);
                    leafNode.Count = _currNGrams[level - 1][_currNGrams[level - 1].Length - appendValues + 1];
                    leafNode.Index = _currNGrams[level - 1][_currNGrams[level - 1].Length - appendValues];
                    ((LeafNode*) FinalPtr)[_currFillLocation[level - 1]] = leafNode;
                }
                else
                {
                    InnerNode innerNode;
                    innerNode.Bow = 0;
                    innerNode.Prob = 0;
                    if (applyFracMKNSmoothing)
                        innerNode.Prob = Util.Int2Float(_currNGrams[level - 1][_currNGrams[level - 1].Length - 1]);
                    innerNode.Child = _prevFillLocation[level];
                    innerNode.Count = _currNGrams[level - 1][_currNGrams[level - 1].Length - appendValues + 1];
                    innerNode.Index = _currNGrams[level - 1][_currNGrams[level - 1].Length - appendValues];
                    ((InnerNode*) InnerPtr)[PAccCount[level - 1] + _currFillLocation[level - 1]] = innerNode;
                }
                _currFillLocation[level - 1]++;
                bool matchPrefix = true;
                if (level < _order)
                {
                    for (int i = 0; i < _currNGrams[level - 1].Length - appendValues + 1; i++)
                        if (_currNGrams[level - 1][i] < _currNGrams[level][i])
                        {
                            matchPrefix = false;
                            break;
                        }
                }
                for (int i = 0; i < _currNGrams[level - 1].Length; i++)
                    _prevNGrams[level - 1][i] = _currNGrams[level - 1][i];
                if (gramMatchIndex < level - 1 && level > 1)
                    RecurseRead(level - 1);
                _prevFillLocation[level - 1] = _currFillLocation[level - 1];
                if (gramMatchIndex < level - 1 && !matchPrefix && _finalAppends[level])
                    continue;
                if (level != _order && matchPrefix)
                    break;
            }
        }
    }
}