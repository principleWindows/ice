// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>The base class for IP-based endpoints: TcpEndpoint, UdpEndpoint.</summary>
    internal abstract class IPEndpoint : Endpoint
    {
        public override string Host { get; }

        public override string? this[string option] =>
            option switch
            {
                "source-address" => SourceAddress?.ToString(),
                "ipv6-only" => IsIPv6Only ? "true" : "false",
                _ => base[option],
            };

        public override ushort Port { get; }

        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || SourceAddress != null;

        // The default port with ice1 is 0.
        protected internal override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultIPPort;

        internal const ushort DefaultIPPort = 4062;

        /// <summary>Whether IPv6 sockets created from this endpoint are dual-mode or IPv6 only.</summary>
        internal bool IsIPv6Only { get; }

        /// <summary>The source address of this IP endpoint.</summary>
        internal IPAddress? SourceAddress { get; }

        public override bool Equals(Endpoint? other) =>
            other is IPEndpoint ipEndpoint &&
                Equals(SourceAddress, ipEndpoint.SourceAddress) &&
                IsIPv6Only == ipEndpoint.IsIPv6Only &&
                base.Equals(other);

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), SourceAddress, IsIPv6Only);

        public override bool IsLocal(Endpoint endpoint)
        {
            if (endpoint is IPEndpoint ipEndpoint)
            {
                // Same as Equals except we don't consider the connection ID

                if (Transport != ipEndpoint.Transport)
                {
                    return false;
                }
                if (Host != ipEndpoint.Host)
                {
                    return false;
                }
                if (Port != ipEndpoint.Port)
                {
                    return false;
                }
                if (!Equals(SourceAddress, ipEndpoint.SourceAddress))
                {
                    return false;
                }
                if (IsIPv6Only != ipEndpoint.IsIPv6Only)
                {
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        protected internal override void WriteOptions(OutputStream ostr)
        {
            if (Protocol == Protocol.Ice1)
            {
                ostr.WriteString(Host);
                ostr.WriteInt(Port);
            }
            else
            {
                Debug.Assert(false); // the derived class must provide the implementation
            }
        }

        public override async ValueTask<IEnumerable<IConnector>> ConnectorsAsync(
            EndpointSelectionType endptSelection,
            CancellationToken cancel)
        {
            Instrumentation.IObserver? observer = Communicator.Observer?.GetEndpointLookupObserver(this);
            observer?.Attach();
            try
            {
                INetworkProxy? networkProxy = Communicator.NetworkProxy;
                int ipVersion = networkProxy?.IPVersion ?? Network.EnableBoth;
                if (networkProxy != null)
                {
                    networkProxy = await networkProxy.ResolveHostAsync(ipVersion, cancel).ConfigureAwait(false);
                }

                IEnumerable<IPEndPoint> addrs =
                    await Network.GetAddressesForClientEndpointAsync(Host,
                                                                     Port,
                                                                     ipVersion,
                                                                     endptSelection,
                                                                     Communicator.PreferIPv6,
                                                                     cancel).ConfigureAwait(false);
                return addrs.Select(item => CreateConnector(item, networkProxy));
            }
            catch (Exception ex)
            {
                observer?.Failed(ex.GetType().FullName ?? "System.Exception");
                throw;
            }
            finally
            {
                observer?.Detach();
            }
        }

        public override IEnumerable<Endpoint> ExpandHost(out Endpoint? publish)
        {
            publish = null;

            // If using a fixed port, this endpoint can be used as the published endpoint to access the returned
            // endpoints. Otherwise, we'll publish each individual expanded endpoint.
            publish = Port > 0 ? this : null;

            IEnumerable<IPEndPoint> addresses = Network.GetAddresses(Host,
                                                                     Port,
                                                                     Network.EnableBoth,
                                                                     EndpointSelectionType.Ordered,
                                                                     Communicator.PreferIPv6);

            if (addresses.Count() == 1)
            {
                return new Endpoint[] { this };
            }
            else
            {
                return addresses.Select(address => Clone(address.Address.ToString(), (ushort)address.Port));
            }
        }

        public override IEnumerable<Endpoint> ExpandIfWildcard()
        {
            int ipv6only = IsIPv6Only ? Network.EnableIPv6 : Network.EnableBoth;
            List<string> hosts = Network.GetHostsForEndpointExpand(Host, ipv6only, false);
            if (hosts.Count == 0)
            {
                return new Endpoint[] { this };
            }
            else
            {
                return hosts.Select(host => Clone(host));
            }
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            if (Protocol == Protocol.Ice1)
            {
                Debug.Assert(Host.Length > 0);

                {
                    sb.Append(" -h ");
                    bool addQuote = Host.IndexOf(':') != -1;
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(Host);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }

                sb.Append(" -p ");
                sb.Append(Port.ToString(CultureInfo.InvariantCulture));

                if (IsIPv6Only)
                {
                    sb.Append(" --ipv6Only");
                }

                if (SourceAddress != null)
                {
                    string sourceAddr = SourceAddress.ToString();
                    bool addQuote = sourceAddr.IndexOf(':') != -1;
                    sb.Append(" --sourceAddress ");
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(sourceAddr);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }
            }
            else
            {
                if (IsIPv6Only)
                {
                    sb.Append($"ipv6-only=true");
                }

                if (SourceAddress != null)
                {
                    sb.Append("source-address=");
                    sb.Append(SourceAddress);
                }
            }
        }

        internal IPEndpoint Clone(ushort port) => port == Port ? this : Clone(Host, port);

        // Constructor for URI parsing.
        private protected IPEndpoint(
            Communicator communicator,
            Protocol protocol,
            string host,
            ushort port,
            Dictionary<string, string> options,
            bool oaEndpoint)
            : base(communicator, protocol)
        {
            Debug.Assert(protocol == Protocol.Ice2);
            if (!oaEndpoint && IPAddress.TryParse(host, out IPAddress? address))
            {
                throw new ArgumentException("0.0.0.0 or [::0] is not a valid host in a proxy endpoint", nameof(host));
            }
            Host = host;
            Port = port;

            if (oaEndpoint)
            {
                if (options.TryGetValue("ipv6-only", out string? value))
                {
                    IsIPv6Only = bool.Parse(value);
                    options.Remove("ipv6-only");
                }
            }
            else // parsing a URI that represents a proxy
            {
                if (options.TryGetValue("source-address", out string? value))
                {
                    // IPAddress.Parse apparently accepts IPv6 addresses in square brackets
                    SourceAddress = IPAddress.Parse(value);
                    options.Remove("source-address");
                }
                else
                {
                    SourceAddress = Communicator.DefaultSourceAddress;
                }
            }
        }

        // Constructor for unmarshaling.
        private protected IPEndpoint(InputStream istr, Protocol protocol)
            : base(istr.Communicator!, protocol)
        {
            Debug.Assert(protocol == Protocol.Ice1 || protocol == Protocol.Ice2);
            Host = istr.ReadString();

            if (Host.Length == 0)
            {
                throw new InvalidDataException("endpoint host is empty");
            }

            if (protocol == Protocol.Ice1)
            {
                checked
                {
                    Port = (ushort)istr.ReadInt();
                }
            }
            else
            {
                Port = istr.ReadUShort();
            }
            SourceAddress = istr.Communicator!.DefaultSourceAddress;
        }

        // Constructor for ice1 endpoint parsing.
        private protected IPEndpoint(
            Communicator communicator,
            Dictionary<string, string?> options,
            bool oaEndpoint,
            string endpointString)
            : base(communicator, Protocol.Ice1)
        {
            if (options.TryGetValue("-h", out string? argument))
            {
                Host = argument ??
                    throw new FormatException($"no argument provided for -h option in endpoint `{endpointString}'");

                if (Host == "*")
                {
                    // TODO: Should we check that IPv6 is enabled first and use 0.0.0.0 otherwise, or will
                    // ::0 just bind to the IPv4 addresses in this case?
                    Host = oaEndpoint ? "::0" :
                        throw new FormatException($"`-h *' not valid for proxy endpoint `{endpointString}'");
                }

                if (!oaEndpoint && IPAddress.TryParse(Host, out IPAddress? address))
                {
                    throw new FormatException("0.0.0.0 or [::0] is not a valid host in a proxy endpoint");
                }

                options.Remove("-h");
            }
            else
            {
                throw new FormatException($"no -h option in endpoint `{endpointString}'");
            }

            if (options.TryGetValue("-p", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -p option in endpoint `{endpointString}'");
                }

                try
                {
                    Port = ushort.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid port value `{argument}' in endpoint `{endpointString}'", ex);
                }
                options.Remove("-p");
            }
            // else Port remains 0

            if (options.TryGetValue("--sourceAddress", out argument))
            {
                if (oaEndpoint)
                {
                    throw new FormatException(
                        $"`--sourceAddress' not valid for an Object Adapter endpoint `{endpointString}'");
                }
                if (argument == null)
                {
                    throw new FormatException(
                        $"no argument provided for --sourceAddress option in endpoint `{endpointString}'");
                }
                try
                {
                    SourceAddress = IPAddress.Parse(argument);
                }
                catch (Exception ex)
                {
                    throw new FormatException(
                        $"invalid IP address provided for --sourceAddress option in endpoint `{endpointString}'", ex);
                }
                options.Remove("--sourceAddress");
            }
            else if (!oaEndpoint)
            {
                SourceAddress = Communicator.DefaultSourceAddress;
            }
            // else SourceAddress remains null

            if (options.TryGetValue("--ipv6Only", out argument))
            {
                if (!oaEndpoint)
                {
                    throw new FormatException(
                        $"`--ipv6Only' not valid for an Object Adapter endpoint `{endpointString}'");
                }
                if (argument != null)
                {
                    throw new FormatException($"--ipv6Only does not accept an argument in endpoint `{endpointString}'");
                }
                IsIPv6Only = true;
                options.Remove("--ipv6Only");
            }
            // else IsIPv6Only remains false (default initialized)
        }

        // Constructor for Clone
        private protected IPEndpoint(IPEndpoint endpoint, string host, ushort port)
            : base(endpoint.Communicator, endpoint.Protocol)
        {
            Host = host;
            Port = port;
            SourceAddress = endpoint.SourceAddress;
            IsIPv6Only = endpoint.IsIPv6Only;
        }

        /// <summary>Creates a clone with the specified host and port.</summary>
        private protected abstract IPEndpoint Clone(string host, ushort port);

        private protected abstract IConnector CreateConnector(EndPoint addr, INetworkProxy? proxy);

        private IPEndpoint Clone(string host) => host == Host ? this : Clone(host, Port);
    }
}
