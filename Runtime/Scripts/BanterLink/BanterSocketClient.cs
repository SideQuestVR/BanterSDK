using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class BanterSocketClient
{
    private const int MagicNumber = unchecked((int)0xDEADBEEF);
    private TcpClient client;
    private NetworkStream stream;
    private BinaryWriter writer;
    private BinaryReader reader;

    public delegate void MessageReceivedHandler(string message);
    public event MessageReceivedHandler MessageReceived;

    public bool IsConnected
    {
        get => client?.Connected ?? false;
    }

    public BanterSocketClient()
    {
    }

    public async Task ConnectAsync(string hostname, int port, MessageReceivedHandler onMessageReceived = null)
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(hostname, port);
            stream = client.GetStream();
            writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            LogLine.Do("Connected to the BanterSocketServer.");

            if (onMessageReceived != null)
            {
                MessageReceived += onMessageReceived;
            }

            _ = Task.Run(() => ListenForMessages());
        }
        catch (Exception ex)
        {
            LogLine.Err($"Failed to connect: {ex.Message}");
        }
    }

    private async Task ListenForMessages()
    {
        try
        {
            while (client != null && client.Connected)
            {
                var message = await ReceiveMessageAsync();
                if (message != null)
                {
                    MessageReceived?.Invoke(message);
                }
            }

            LogLine.Do("BanterSocketClient no longer listening for messages");
        }
        catch (Exception ex)
        {
            LogLine.Err($"Listening error: {ex.Message}");
        }
    }

    public void SendMessageAsync(string message)
    {
        if (client == null || !client.Connected)
        {
            LogLine.Err("Not connected to the server.");
            return;
        }

        try
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            writer.Write(MagicNumber); // Magic number
            writer.Write(messageBytes.Length); // Message length
            writer.Write(messageBytes); // Message
            writer.Flush();
            Debug.Log("Message sent.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to send message: {ex.Message}");
        }
    }

    private async Task<string> ReceiveMessageAsync()
    {
        try
        {
            if (client == null || !client.Connected)
            {
                LogLine.Err("Not connected to the server.");
                return null;
            }

            int magicNumber = reader.ReadInt32();// await ReadIntAsync(reader);
            if (magicNumber != MagicNumber)
            {
                magicNumber = await RealignToMagicNumberAsync(magicNumber);
                if (magicNumber != MagicNumber)
                {
                    LogLine.Err("Failed to realign to MAGIC_NUMBER.");
                    return null;
                }
            }

            var length = reader.ReadInt32();// await ReadIntAsync(reader);
            var messageBytes = new byte[length];
            int bytesRead = 0;
            while (bytesRead < length)
            {
                int read = await reader.BaseStream.ReadAsync(messageBytes, bytesRead, length - bytesRead);
                if (read == 0)
                {
                    LogLine.Err("Connection closed.");
                    return null;
                }
                bytesRead += read;
            }

            return Encoding.UTF8.GetString(messageBytes);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return null;
        }
    }

    private async Task<int> RealignToMagicNumberAsync(int initialMagic)
    {
        int count = initialMagic;
        byte[] buffer = new byte[1];

        while (true)
        {
            await reader.BaseStream.ReadAsync(buffer, 0, 1);
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            count = (int)(((count << 8) & 0xFFFFFF00) | (buffer[0] & 0xFF));
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand

            if (count == MagicNumber)
            {
                return count;
            }
        }
    }

    //private async Task<int> ReadIntAsync(BinaryReader reader)
    //{
    //    byte[] intBytes = new byte[4];
    //    await reader.BaseStream.ReadAsync(intBytes, 0, 4);
    //    Array.Reverse(intBytes); // Reverse if necessary for endianness
    //    return BitConverter.ToInt32(intBytes, 0);
    //}

    public void Disconnect()
    {
        writer?.Dispose();
        reader?.Dispose();
        stream?.Dispose();
        client?.Close();
        client = null;
        Debug.Log("Disconnected from the server.");
    }
}