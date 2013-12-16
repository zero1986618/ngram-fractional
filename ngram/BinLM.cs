namespace ngram
{
    public struct LMHead
    {
        public uint Signature;
        public int Order;
        public int UnkID;
    }

    internal struct InnerProbNode
    {
        public int Index;
        public float Prob;
        public long Child;
        public float Bow;
    }

    internal struct LeafProbNode
    {
        public int Index;
        public float Prob;
    }

    internal struct InnerNode
    {
        public int Index;
        public int Count;
        public float Prob;
        public float Bow;
        public long Child;
    }

    internal struct LeafNode
    {
        public float Prob;
        public int Index;
        public int Count;
    }
}