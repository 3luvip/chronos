public static class Convert
{
    public static byte[] ToMapBytes(byte[,] data)
    {
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);
        var bytes = new byte[4 + rows * cols];

        bytes[0] = (byte)(cols >> 8);
        bytes[1] = (byte)(cols & 0xFF);
        bytes[2] = (byte)(rows >> 8);
        bytes[3] = (byte)(rows & 0xFF);

        int idx = 4;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            bytes[idx++] = data[r, c];

        return bytes;

    }
}