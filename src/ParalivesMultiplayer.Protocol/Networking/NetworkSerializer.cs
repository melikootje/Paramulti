using System;
using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking
{
    public static class NetworkSerializer
    {
        const int HeaderSize = 5;

        public static void WriteMessage(Stream stream, MessageBase msg)
        {
            var codeBytes = msg.MessageCodeBytes;

            using var payload = new MemoryStream();
            using (var pw = new BinaryWriter(payload, Encoding.UTF8))
            {
                pw.Write(codeBytes);
                msg.Encode(pw);
            }

            byte[] payloadData = payload.ToArray();
            var header = new byte[HeaderSize];
            BitConverter.GetBytes(payloadData.Length).CopyTo(header, 0);
            header[4] = (byte)codeBytes.Length;

            stream.Write(header, 0, HeaderSize);
            stream.Write(payloadData, 0, payloadData.Length);
            stream.Flush();
        }

        public static bool TryReadMessage(Stream stream, out MessageBase message)
        {
            message = null;
            var header = new byte[HeaderSize];

            if (ReadFully(stream, header, 0, HeaderSize) != HeaderSize)
                return false;

            int totalLength = BitConverter.ToInt32(header, 0);
            byte codeLen = header[4];

            if (totalLength <= 0 || codeLen > totalLength)
                return false;

            var body = new byte[totalLength];
            if (ReadFully(stream, body, 0, totalLength) != totalLength)
                return false;

            string messageCode = Encoding.UTF8.GetString(body, 0, codeLen);

            if (!MessageRegistry.TryGetHandler(messageCode, out var prototype))
                return false;

            using var ms = new MemoryStream(body, codeLen, totalLength - codeLen);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            return prototype.TryDecode(reader, out message);
        }

        static int ReadFully(Stream stream, byte[] buffer, int offset, int length)
        {
            int remaining = length;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset + (length - remaining), remaining);
                if (read <= 0) break;
                remaining -= read;
            }
            return length - remaining;
        }
    }
}
