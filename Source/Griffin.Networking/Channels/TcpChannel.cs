﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Griffin.Networking.Buffers;
using Griffin.Networking.Channel;
using Griffin.Networking.Messages;

namespace Griffin.Networking.Channels
{
    /// <summary>
    /// TCP networking channel
    /// </summary>
    public class TcpChannel : IChannel, IDisposable
    {
        private readonly IPipeline _pipeline;
        private readonly BufferSlice _readBuffer;
        private Socket _socket;
        private Stream _stream;
        private readonly ILogger _logger = LogManager.GetLogger<TcpChannel>();
        private MemoryStream _readStream;

        public TcpChannel(IPipeline pipeline)
        {
            _pipeline = pipeline;
            Pipeline.SetChannel(this);
            _readBuffer = new BufferSlice(new byte[65535], 0, 65535, 0);
        }

        public TcpChannel(IPipeline pipeline, BufferPool pool)
        {
            _pipeline = pipeline;
            Pipeline.SetChannel(this);
            _readBuffer = pool.PopSlice();
            _stream = new PeekableMemoryStream(_readBuffer.Buffer, _readBuffer.StartOffset, _readBuffer.Capacity);
        }

        protected IPipeline Pipeline
        {
            get { return _pipeline; }
        }

        public ILogger Logger
        {
            get { return _logger; }
        }

        #region IChannel Members

        /// <summary>
        /// A message have been sent through the pipeline and are ready to be handled by the channel.
        /// </summary>
        /// <param name="message">Message that the channel should process.</param>
        public virtual void HandleDownstream(IPipelineMessage message)
        {
            Logger.Debug("Got message " + message);

            if (message is SendMessage)
                Send((SendMessage) message);
            if (message is Disconnect)
                HandleDisconnect(new SocketException((int) SocketError.Success));
            else if (message is Close)
                Dispose(true);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            Logger.Debug("Disposing us");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        public void AssignSocket(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException("socket");

            _stream = CreateStream(socket);
            _socket = socket;
        }

        public virtual Stream CreateStream(Socket socket)
        {
            return new NetworkStream(socket);
        }

        private void OnRead(IAsyncResult ar)
        {
            int bytesRead;
            try
            {
                bytesRead = _stream.EndRead(ar);
                Logger.Trace("Read " + bytesRead + " bytes.");
            }
            catch (Exception err)
            {
                Logger.Warning("Got disconnected", err);
                HandleDisconnect(err);
                Dispose();
                return;
            }

            if (HandleReadBytes(bytesRead))
                StartRead();
        }

        protected void StartRead()
        {
            try
            {
                //var remainingCapacity = _readBuffer.Capacity - (_readBuffer.CurrentOffset - _readBuffer.StartOffset);
                Logger.Debug("Reading from " + _readBuffer.CurrentOffset + " length " + _readBuffer.RemainingCapacity);
                _stream.BeginRead(_readBuffer.Buffer, _readBuffer.StartOffset, _readBuffer.RemainingCapacity, OnRead, null);
            }
            catch (Exception err)
            {
                Logger.Warning("Failed to read.", err);
                HandleDisconnect(err);
                Dispose();
            }
        }

        private bool HandleReadBytes(int bytesRead)
        {
            try
            {
                if (bytesRead == 0)
                {
                    HandleDisconnect(null);
                    Dispose();
                    return false;
                }

                _readBuffer.Count += bytesRead;
                var lastOffset = -1;
                while (_readBuffer.RemainingLength != 0 && lastOffset != _readBuffer.CurrentOffset)
                {
                    lastOffset = _readBuffer.CurrentOffset;
                    var msg = new Received(_socket.RemoteEndPoint, _stream, _readBuffer);
                    Pipeline.SendUpstream(msg);
                }

                _logger.Debug(string.Format("Compacting since we got {1} bytes left from pos {0}.", _readBuffer.CurrentOffset, _readBuffer.RemainingLength));
                _readBuffer.Compact();
                return true;
            }
            catch (Exception err)
            {
                Logger.Warning("A pipeline handler threw an exception.", err);
                Pipeline.SendUpstream(new PipelineFailure(err));
                Dispose();
                return false;
            }
        }


        private void HandleDisconnect(Exception err)
        {
            Logger.Warning("Got disconnected", err);
            Pipeline.SendUpstream(new Disconnected(err));
        }

        public virtual void Send(SendMessage message)
        {
            if (_socket == null)
                throw new InvalidOperationException("Socket is disconnected");


            var buffer = message.BufferSlice;
            try
            {
                var tmp = Encoding.UTF8.GetString(buffer.Buffer, buffer.StartOffset, buffer.Count);
                Logger.Trace(tmp);
                Logger.Debug("Sending " + buffer.Count + " bytes");
                _stream.BeginWrite(buffer.Buffer, buffer.StartOffset, buffer.Count, OnSent, message.BufferSlice);
            }
            catch (Exception err)
            {
                HandleDisconnect(err);
                Dispose();
            }
        }

        private void OnSent(IAsyncResult ar)
        {
            try
            {
                _stream.EndWrite(ar);
                Logger.Trace("Send completed");
                Pipeline.SendUpstream(new Sent((BufferSlice) ar.AsyncState));
            }
            catch (Exception err)
            {
                HandleDisconnect(err);
                Dispose();
            }
        }

        public virtual void Dispose(bool disposing)
        {
            if (!disposing || _socket == null)
                return;

            Pipeline.SendUpstream(new Closed());

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Disconnect(true);
            _socket.Close();
            _socket = null;

            _stream.Close();
            _stream.Dispose();

            if (_readBuffer is IDisposable)
                ((IDisposable) _readBuffer).Dispose();
        }
    }
}