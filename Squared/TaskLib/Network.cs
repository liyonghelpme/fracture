﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;

namespace Squared.Task {
    public static class Network {
        public static Future ConnectTo (string host, int port) {
            var f = new Future();
            TcpClient client = new TcpClient();
            client.BeginConnect(host, port, (ar) => {
                client.EndConnect(ar);
                f.Complete(client);
            }, null);
            return f;
        }
    }

    public static class NetworkExtensionMethods {
        public static Future AcceptIncomingConnection (this TcpListener listener) {
            var f = new Future();
            listener.BeginAcceptTcpClient((ar) => {
                TcpClient result = listener.EndAcceptTcpClient(ar);
                f.Complete(result);
            }, null);
            return f;
        }
    }
}
