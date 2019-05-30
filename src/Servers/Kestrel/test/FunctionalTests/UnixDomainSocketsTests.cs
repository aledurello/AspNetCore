// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Testing;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class UnixDomainSocketsTest : TestApplicationErrorLoggerLoggedTest
    {
#if LIBUV
        [OSSkipCondition(OperatingSystems.Windows, SkipReason = "Libuv does not support unix domain sockets on Windows.")]
#else
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win8, WindowsVersions.Win81, WindowsVersions.Win2008R2, SkipReason = "UnixDomainSocketEndPoint is not supported on older versions of Windows")]
#endif
        [ConditionalFact]
        public async Task TestUnixDomainSocket()
        {
            var path = Path.GetTempFileName();

            Delete(path);

            try
            {
                async Task EchoServer(ConnectionContext connection)
                {
                    // For graceful shutdown
                    var notificationFeature = connection.Features.Get<IConnectionLifetimeNotificationFeature>();

                    try
                    {
                        while (true)
                        {
                            var result = await connection.Transport.Input.ReadAsync(notificationFeature.ConnectionClosedRequested);

                            if (result.IsCompleted)
                            {
                                break;
                            }

                            await connection.Transport.Output.WriteAsync(result.Buffer.ToArray());

                            connection.Transport.Input.AdvanceTo(result.Buffer.End);
                        }
                    }
                    catch (OperationCanceledException)
                    {

                    }
                }

                var hostBuilder = TransportSelector.GetWebHostBuilder()
                    .UseKestrel(o =>
                    {
                        o.ListenUnixSocket(path, builder =>
                        {
                            builder.Run(EchoServer);
                        });
                    })
                    .ConfigureServices(AddTestLogging)
                    .Configure(c => { });

                using (var host = hostBuilder.Build())
                {
                    await host.StartAsync();

                    using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                    {
                        socket.Connect(new UnixDomainSocketEndPoint(path));

                        var data = Encoding.ASCII.GetBytes("Hello World");
                        socket.Send(data);

                        var buffer = new byte[data.Length];
                        var read = 0;
                        while (read < data.Length)
                        {
                            read += socket.Receive(buffer, read, buffer.Length - read, SocketFlags.None);
                        }

                        Assert.Equal(data, buffer);
                    }

                    await host.StopAsync();
                }
            }
            finally
            {
                Delete(path);
            }
        }

        private static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (FileNotFoundException)
            {

            }
        }
    }
}
