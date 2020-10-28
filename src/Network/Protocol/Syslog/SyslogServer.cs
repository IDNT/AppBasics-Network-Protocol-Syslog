/*
 * Copyright 2020 IDNT Europe GmbH (http://idnt.net)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IDNT.AppBase.Network.Protocol.Syslog
{
    public class SyslogServer : IDisposable
    {
        public const int DEFAULT_SYSLOG_PORT = 514;
        private const int SOCKET_CLOSE_TIMEOUT = 3;
        public const int DEFAULT_READ_TIMEOUT = 10000;
        public const int DEFAULT_LISTEN_BACKLOG = 100;
        public const int DEFAULT_MAX_MSG_SIZE = 2048;
        public const int DEFAULT_MIN_MSG_SIZE = 480;

        private int _tcpListenPort;
        private int _udpListenPort;
        private IPAddress _listenAddress;
        private int _listenBacklog;
        private TimeSpan _readTimeout;
        private int _maxMsgSize;

        #region CTOR
        /// <summary>
        /// Syslog Server
        /// </summary>
        /// <param name="listenAddress">Interface to bind on. Default is loopback only.</param>
        /// <param name="udpListenPort">UDP listen port or 0 to disable UDP listener. Default is SyslogServer.DEFAULT_SYSLOG_PORT.</param>
        /// <param name="tcpListenPort">TCP listen port or 0 to disable TCP listener.Default is SyslogServer.DEFAULT_SYSLOG_PORT.</param>
        /// <param name="listenBacklog">Connection backlog. Default is SyslogServer.DEFAULT_LISTEN_BACKLOG.</param>
        /// <param name="readTimeout">Socket read timeout in milliseconds. Default is SyslogServer.DEFAULT_READ_TIMEOUT.</param>
        /// <param name="maxMessageSize">Maximum message size in byte. Default is SyslogServer.DEFAULT_MAX_MSG_SIZE. The minimum is SyslogServer.DEFAULT_MIN_MSG_SIZE as defined by the RFC.</param>
        public SyslogServer(
                IPAddress listenAddress = null, 
                int udpListenPort = DEFAULT_SYSLOG_PORT, 
                int tcpListenPort = DEFAULT_SYSLOG_PORT, 
                int listenBacklog = DEFAULT_LISTEN_BACKLOG,
                int readTimeout = DEFAULT_READ_TIMEOUT,
                int maxMessageSize = DEFAULT_MAX_MSG_SIZE)
            : this(listenAddress, udpListenPort, tcpListenPort, listenBacklog, TimeSpan.FromMilliseconds(readTimeout), maxMessageSize)
        {
        }

        /// <summary>
        /// Syslog Server
        /// </summary>
        /// <param name="listenAddress">Interface to bind on. Default is loopback only.</param>
        /// <param name="udpListenPort">UDP listen port or 0 to disable UDP listener. Default is SyslogServer.DEFAULT_SYSLOG_PORT.</param>
        /// <param name="tcpListenPort">TCP listen port or 0 to disable TCP listener.Default is SyslogServer.DEFAULT_SYSLOG_PORT.</param>
        /// <param name="listenBacklog">Connection backlog. Default is SyslogServer.DEFAULT_LISTEN_BACKLOG.</param>
        /// <param name="readTimeout">Socket read timeout in milliseconds. Default is SyslogServer.DEFAULT_READ_TIMEOUT.</param>
        /// <param name="maxMessageSize">Maximum message size in byte. Default is SyslogServer.DEFAULT_MAX_MSG_SIZE. The minimum is SyslogServer.DEFAULT_MIN_MSG_SIZE as defined by the RFC.</param>
        public SyslogServer(
                IPAddress listenAddress,
                int udpListenPort,
                int tcpListenPort,
                int listenBacklog,
                TimeSpan readTimeout,
                int maxMessageSize)
        {
            ListenAddress = listenAddress;
            ListenBacklog = listenBacklog;
            UdpListenPort = udpListenPort;
            TcpListenPort = tcpListenPort;
            ReadTimout = readTimeout;
            MaxMessageSize = maxMessageSize;
        }
        #endregion

        #region Properties

        public IPAddress ListenAddress
        {
            get { return _listenAddress; }
            set
            {
                _listenAddress = value ?? IPAddress.Loopback;
            }
        }

        public int ListenBacklog
        {
            get { return _listenBacklog; }
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException("ListenBacklog");
                _listenBacklog = value;
            }
        }


        public int TcpListenPort 
        { 
            get { return _tcpListenPort; }
            set
            {
                if (value > 65535)
                    throw new ArgumentOutOfRangeException("TcpListenPort");
                _tcpListenPort = value;
            }
        }

        public int UdpListenPort
        {
            get { return _udpListenPort; }
            set
            {
                if (value > 65535)
                    throw new ArgumentOutOfRangeException("UdpListenPort");
                _udpListenPort = value;
            }
        }

        /// <summary>
        /// Read timeout
        /// </summary>
        public TimeSpan ReadTimout
        {
            get { return _readTimeout; }
            set { _readTimeout = value; }
        }

        public int MaxMessageSize
        {
            get { return _maxMsgSize; }
            set
            {
                if (value < DEFAULT_MIN_MSG_SIZE)
                    throw new ArgumentOutOfRangeException("MaxMessageSize");
                _maxMsgSize = value; 
            }
        }
        #endregion

        #region Control
        private Int32 _opLock = 0;
        private CancellationTokenSource _cts;
        private Task _listenerTask;

        public void Start(Func<SyslogMessage, CancellationToken, Task> callback)
        {
            Start(callback, CancellationToken.None);
        }

        public void Start(Func<SyslogMessage, CancellationToken, Task> callback, CancellationToken cancellationToken)
        {
            if (_isDisposing != 0)
                throw new ObjectDisposedException(typeof(SyslogServer).Name);

            if (Interlocked.CompareExchange(ref _opLock, 1, 0) == 1)
                throw new InvalidOperationException("Operation in progress.");

            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                List<Task> tasks = new List<Task>();
                if (TcpListenPort > 0)
                    tasks.Add(Task.Run(() => SocketListener(TcpListenPort, ProtocolType.Tcp, _cts.Token, callback)));
                if (UdpListenPort > 0)
                    tasks.Add(Task.Run(() => SocketListener(UdpListenPort, ProtocolType.Udp, _cts.Token, callback)));

                _listenerTask = Task.WhenAll(tasks);
            }
            finally
            {
                _opLock = 0;
            }
        }

        public void Stop()
        {
            if (Interlocked.CompareExchange(ref _opLock, 1, 0) == 1)
                throw new InvalidOperationException("Operation in progress.");

            Task waitFor;
            try
            {
                
                if (_listenerTask == null)
                    return;

                _cts.Cancel(false);

                waitFor = _listenerTask;
                _listenerTask = null;
            }
            finally
            {
                _opLock = 0;
            }

            try
            {
                waitFor?.Wait();
            }
            catch(OperationCanceledException)
            {
            }
        }

        #endregion

        #region Private
        private async Task<Socket> AcceptAsync(Socket socket)
        {
            try
            {
                return await Task<Socket>.Factory.FromAsync(socket.BeginAccept, socket.EndAccept, null).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"{ex.Message} ({ex.GetType()})");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Trace.WriteLine($"{ex.Message} ({ex.GetType()})");
                return null;
            }
        }

        private async Task<int> TcpReceiveBufferAsync(
            Socket socket,
            byte[] buffer,
            int offset,
            int size,
            SocketFlags socketFlags)
        {
            int bytesReceived = 0;
            try
            {
                var asyncResult = socket.BeginReceive(buffer, offset, size, socketFlags, null, null);

                var receiveTask = Task<int>.Factory.FromAsync(asyncResult, _ => socket.EndReceive(asyncResult));

                if (receiveTask == await Task.WhenAny(receiveTask, Task.Delay(_readTimeout)).ConfigureAwait(false))
                {
                    bytesReceived = await receiveTask.ConfigureAwait(false);
                }
                else
                {
                    throw new TimeoutException();
                }
            }
            catch (ObjectDisposedException)
            {
                return -2;
            }
            catch (OperationCanceledException)
            {
                return -2;
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"{ex.Message} ({ex.GetType()})");
                return -1;
            }
            catch (TimeoutException ex)
            {
                Trace.WriteLine($"{ex.Message} ({ex.GetType()})");
                return -2;
            }

            return bytesReceived;
        }

        private async Task<int> UdpReceiveBufferAsync(
           Socket socket,
           byte[] buffer,
           int offset,
           int size,
           EndPoint sender,
           SocketFlags socketFlags)
        {
            int bytesReceived = 0;
            try
            {
                var asyncResult = socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref sender, null, null);

                var receiveTask = Task<int>.Factory.FromAsync(asyncResult, _ => socket.EndReceiveFrom(asyncResult, ref sender));

                if (receiveTask == await Task.WhenAny(receiveTask, Task.Delay(_readTimeout)).ConfigureAwait(false))
                {
                    bytesReceived = await receiveTask.ConfigureAwait(false);
                }
                else
                {
                    return -2;
                }
            }
            catch (ObjectDisposedException)
            {
                return -2;
            }
            catch (OperationCanceledException)
            {
                return -2;
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"{ex.Message} ({ex.GetType()})");
                return -1;
            }

            return bytesReceived;
        }

        private async Task TcpReceiveMessageAsync(Socket socket, CancellationToken cancellationToken, Func<SyslogMessage, CancellationToken, Task> callback)
        {
            byte[] buffer = new byte[1024];
            int readSize = 0;
            StringBuilder txt = new StringBuilder();
            

            try
            {
                while (socket.Connected && (readSize = await TcpReceiveBufferAsync(socket, buffer, 0, buffer.Length, SocketFlags.None)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (txt.Length + readSize > MaxMessageSize)
                    {
                        Trace.WriteLine("Maximum message size exceeded. Closing socket.");
                        return;
                    }

                    var s = Encoding.ASCII.GetString(buffer, 0, readSize);

                    int idx = 0;
                    if ((idx = s.IndexOf("\n")) > -1)
                    {
                        txt.Append(s.Substring(0, idx));
                        SyslogMessage sm = null;
                        if (SyslogMessage.TryParse(((IPEndPoint)socket.RemoteEndPoint).Address, txt.ToString(), out sm))
                        {
                            await callback(sm, cancellationToken);
                        }

                        txt = new StringBuilder(s.Substring(idx + 1));
                        continue;
                    }
                    else
                    {
                        txt.Append(s);
                    }
                }

                if (txt.Length == 0)
                    return; // Timeout or socket error

                SyslogMessage msg;
                if (SyslogMessage.TryParse(((IPEndPoint)socket.RemoteEndPoint).Address, txt.ToString(), out msg))
                {
                    await callback(msg, cancellationToken);
                }
            }
            finally
            {
                try { if (socket.Connected) socket.Close(); } catch (SocketException) { }
            }
        }

        private async Task UdpReceiveMessagesAsync(Socket socket, CancellationToken cancellationToken, Func<SyslogMessage, CancellationToken, Task> callback)
        {
            byte[] buffer = new byte[1024];
            int readSize = 0;

            try
            {
                EndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);

                while ((readSize = await UdpReceiveBufferAsync(socket, buffer, 0, 1024, sender, SocketFlags.None)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (readSize > MaxMessageSize)
                    {
                        Trace.WriteLine("Maximum message size exceeded. Closing socket.");
                        return;
                    }

                    SyslogMessage sm = null;
                    if (SyslogMessage.TryParse(((IPEndPoint)sender).Address, Encoding.ASCII.GetString(buffer, 0, readSize), out sm))
                    {
                        await callback(sm, cancellationToken);
                    }
                }
            }
            finally
            {
                try { if (socket.Connected) socket.Close(); } catch (SocketException ex) { Trace.WriteLine($"{ex.Message} ({ex.GetType()})"); }
            }
        }

        private async void SocketListener(int port, ProtocolType protocol, CancellationToken cancellationToken, Func<SyslogMessage, CancellationToken, Task> callback)
        {
            var endPoint = new IPEndPoint(_listenAddress, port);
            var listenSocket = new Socket(_listenAddress.AddressFamily, protocol == ProtocolType.Udp ? SocketType.Dgram : SocketType.Stream, protocol);
            listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listenSocket.Bind(endPoint);
            if (protocol == ProtocolType.Tcp)
                listenSocket.Listen(_listenBacklog);

            cancellationToken.Register(() =>
            {
                try { listenSocket?.Close(SOCKET_CLOSE_TIMEOUT); }
                catch (InvalidOperationException) { }
            });

            while (!cancellationToken.IsCancellationRequested)
            {
                if (protocol == ProtocolType.Tcp)
                {
                    var accepted = await AcceptAsync(listenSocket).ConfigureAwait(false);

                    if (accepted == null)
                        continue;
    
                    var _ = Task.Run(async () => await TcpReceiveMessageAsync(accepted, cancellationToken, callback));
                }
                else if (protocol == ProtocolType.Udp)
                {
                    await UdpReceiveMessagesAsync(listenSocket, cancellationToken, callback);
                }
            }
        }
        #endregion

        #region IDisposable implementation
        private Int32 _isDisposing = 0;

        void IDisposable.Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposing, 1, 0) == 1)
                return;

            Stop();
        }
        #endregion
    }
}

