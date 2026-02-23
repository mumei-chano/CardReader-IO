using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Bngrw
{
    public sealed class BngRwClient : IDisposable
    {
        public event Action<string>? RawLineReceived;
        public event Action<string>? StatusReceived;
        public event Action<string>? CardIdmReceived;

        private SerialPort? _sp;
        private Thread? _rxThread;
        private CancellationTokenSource? _cts;

        private string? OpenedPort;

        public bool IsOpen => _sp?.IsOpen == true;
        /// <summary>
        /// カードリーダーと接続します。
        /// </summary>
        /// <param name="portName"> シリアルポート名(COM1,2,3,4....これらはデバイスマネージャーでチェックしてください) </param>
        /// <param name="baudRate"> 115200を使用してください </param>
        /// <param name="openDelayMs"> カードリーダーの起動を待つ </param>
        public void Open(string portName, int baudRate = 115200, int openDelayMs = 1500)
        {
            if (IsOpen) return;

            OpenedPort = portName;

            _sp = new SerialPort(portName, baudRate)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\n",
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 500,
                WriteTimeout = 500,
            };

            _sp.Open();

            Thread.Sleep(openDelayMs);

            _cts = new CancellationTokenSource();

            _rxThread = new Thread(() => RxLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "BngRwClient.Rx"
            };
            _rxThread.Start();
        }
        /// <summary>
        /// カードリーダーに命令を送信します (Timeout Exceptionによるカクつきを防止するため、選曲画面前でCloseしてください)
        /// </summary>
        /// <param name="line"></param>
        public void Send(string line)
        {
            if (_sp == null || !_sp.IsOpen)
            {
                try
                {
                    Open(OpenedPort);
                    _sp.Write(line.Trim() + "\n");
                }
                catch
                {

                }
            }
        }
        /// <summary>
        /// カードリーダーとの接続を切断します。
        /// </summary>
        public void Close()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                if (_rxThread != null && _rxThread.IsAlive)
                    _rxThread.Join(300);
            }
            catch { }

            try
            {
                if (_sp != null)
                {
                    if (_sp.IsOpen) _sp.Close();
                    _sp.Dispose();
                }
            }
            catch { }

            _sp = null;
            _rxThread = null;

            _cts?.Dispose();
            _cts = null;
        }

        private void RxLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_sp == null) break;

                    var line = _sp.ReadLine().TrimEnd('\r', '\n');
                    if (line.Length == 0) continue;

                    RawLineReceived?.Invoke(line);

                    if (line.StartsWith("STAT:", StringComparison.OrdinalIgnoreCase))
                    {
                        var stat = line.Substring(5).Trim();
                        if (stat.Length > 0) StatusReceived?.Invoke(stat);
                        continue;
                    }

                    if (line.StartsWith("IDM:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idm = line.Substring(4).Trim().ToUpperInvariant();
                        idm = idm.Trim();
                        if (idm.Length > 0) CardIdmReceived?.Invoke(idm);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    RawLineReceived?.Invoke("RXERR:" + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
