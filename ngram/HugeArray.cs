namespace ngram
{
    internal class HugeArray<T>
    {
        public HugeArray(int size)
        {
            this.size = size;
            int rows = size / maxAllocateSize + 1;
            elements = new T[rows][];
            for (int i = 0; i < elements.Length - 1; i++)
                elements[i] = new T[maxAllocateSize];
            elements[elements.Length - 1] = new T[size % maxAllocateSize];
        }
       
        public int Length
        {
            get { return size; }
        }

        public T this[int index]
        {
            get
            {
                int row = index / maxAllocateSize;
                return elements[row][index % maxAllocateSize];
            }
            set
            {
                int row = index / maxAllocateSize;
                elements[row][index % maxAllocateSize] = value;
            }
        }
        private int size;
        private const int maxAllocateSize = 0x1FFFFFF0;
        private T[][] elements;
    }
}