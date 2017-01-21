namespace TileServer.Http
{
    public static class ArrayUtil
    {
        /// <summary>Looks for the next occurrence of a sequence in a byte array</summary>
        /// <param name="array">Array that will be scanned</param>
        /// <param name="start">Index in the array at which scanning will begin</param>
        /// <param name="sequence">Sequence the array will be scanned for</param>
        /// <returns>
        ///   The index of the next occurrence of the sequence of -1 if not found
        /// </returns>
        /// <remarks>
        /// Taken from http://stackoverflow.com/a/39021296/1025421
        /// </remarks>
        private static int FindSequence(byte[] array, int start, byte[] sequence)
        {
            var end = array.Length - sequence.Length; // past here no match is possible
            var firstByte = sequence[0]; // cached to tell compiler there's no aliasing

            while (start < end)
            {
                // scan for first byte only. compiler-friendly.
                if (array[start] == firstByte)
                {
                    // scan for rest of sequence
                    for (int offset = 1; offset < sequence.Length; ++offset)
                    {
                        if (array[start + offset] != sequence[offset])
                        {
                            break; // mismatch? continue scanning with next byte
                        }

                        if (offset == sequence.Length - 1)
                        {
                            return start; // all bytes matched!
                        }
                    }
                }

                start++;
            }

            // end of array reached without match
            return -1;
        }
    }
}
