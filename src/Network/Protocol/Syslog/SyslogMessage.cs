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
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace IDNT.AppBase.Network.Protocol.Syslog
{
    /// <summary>
    /// Syslog message according to RFC5424 
    /// </summary>
    [DataContract(Name = "syslogMessage")]
    public class SyslogMessage
    {
        private static readonly Regex _reRFC5234 = new Regex(@"^
(?<PRI>\<\d{1,3}\>)?
(?<VER>\d{1,2})?
\x20(?<TS>[^\s]+)?
\x20(?<HOST>[^\s]+)?
\x20(?<APP>[^\s]+)?
\x20(?<PID>[^\s]+)?
\x20(?<MSGID>[^\s]+)?
\x20(?<SD>\[.+\])?
[\x20]*(?<MSG>.+)
", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex _reSD = new Regex(@"^(
\s*
(?<term>
    ((?<prefix>[a-zA-Z][a-zA-Z0-9-_]*)\=)?
    (?<termString>
        (?<quotedTerm>
            (?<quote>['""])
            ((\\\k<quote>)|((?!\k<quote>).))*
            \k<quote>?
        )
        |(?<simpleTerm>[^\s]+)
    )
)
\s*)*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture);

        private readonly Dictionary<string, string> _data;

        public SyslogMessage()
        {
            Version = 1;
            Facility = SyslogFacility.User;
            Severity = SyslogSeverity.Notice;
            ReceivedAt = Timestamp = DateTime.Now;
            _data = new Dictionary<string, string>();
        }

        public SyslogMessage(IPAddress sender)
            : this()
        {
            sender = sender ?? IPAddress.Loopback;

            try
            {
                IPHostEntry he = Dns.GetHostEntry(sender);
                if (he != null)
                    Hostname = he.HostName;
            }
            catch (SocketException)
            {
                Hostname = sender.ToString();
            }
        }

        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "facility")]
        public SyslogFacility Facility { get; set; }

        [DataMember(Name = "severity")]
        public SyslogSeverity Severity { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "hostname")]
        public string Hostname { get; set; }

        [DataMember(Name = "appName")]
        public string AppName { get; set; }

        [DataMember(Name = "procId")]
        public string ProcId { get; set; }

        [DataMember(Name = "msgId")]
        public string MsgId { get; set; }

        [DataMember(Name = "content")]
        public string Content { get; set; }

        [DataMember(Name = "remoteAddr")]
        public string RemoteAddr { get; set; }

        [DataMember(Name = "receivedAt")]
        public DateTime ReceivedAt { get; set; }

        [DataMember(Name = "data")]
        public IDictionary<string, string> Data => _data;

        public override string ToString()
        {
            var sd = string.Join(" ", _data.Select(t => t.Key + (!string.IsNullOrEmpty(t.Value) ? "=\"" + t.Value + "\"" : "")));
            return $"<{((int)Facility * 8) + (int)Severity}>{Version} {Timestamp.ToString("o")} {Hostname} {AppName ?? "-"} {ProcId ?? "-"} {MsgId ?? "-"} "
                    + (!string.IsNullOrEmpty(sd) ? " ["+sd+"]" : "")
                    + " "+Content;
            
        }

        static public bool TryParse(IPAddress sender, byte[] message, out SyslogMessage logmsg)
        {
            return TryParse(sender, Encoding.ASCII.GetString(message), out logmsg);
        }

        static public bool TryParse(IPAddress sender, string message, out SyslogMessage logmsg)
        {
            logmsg = new SyslogMessage(sender);

            Match msgMatch;
            if (sender == null || string.IsNullOrEmpty(message))
                return false;

            if (!(msgMatch = _reRFC5234.Match(message)).Success)
            {
                Trace.WriteLine($"Could not understand received message '{message}'.");
                return false;
            }

            string strValue;
            int intValue;
            DateTime dtValue;

            if (msgMatch.Groups["PRI"].Success && 
                    !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["PRI"].Value)) &&
                    Int32.TryParse(strValue.Substring(1, strValue.Length-2), out intValue))
            {
                logmsg.Facility = (SyslogFacility)Math.Floor((double)intValue / 8);
                logmsg.Severity = (SyslogSeverity)(intValue % 8);
            }

            if (msgMatch.Groups["VER"].Success &&
                !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["VER"].Value)) &&
                Int32.TryParse(strValue, out intValue))
                logmsg.Version = intValue;

            if (msgMatch.Groups["TS"].Success &&
                    !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["TS"].Value)) &&
                    DateTime.TryParse(strValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out dtValue))
                logmsg.Timestamp = dtValue;

            if (msgMatch.Groups["HOST"].Success &&
                !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["HOST"].Value)))
                logmsg.Hostname = strValue;

            if (msgMatch.Groups["APP"].Success &&
               !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["APP"].Value)))
                logmsg.AppName = strValue;

            if (msgMatch.Groups["PID"].Success &&
             !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["PID"].Value)))
                logmsg.ProcId = strValue;

            if (msgMatch.Groups["MSGID"].Success &&
                !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["MSGID"].Value)))
                logmsg.MsgId = strValue;

            if (msgMatch.Groups["SD"].Success &&
                !string.IsNullOrWhiteSpace((strValue = msgMatch.Groups["SD"].Value)))
            {
                var sdMatch = _reSD.Match(msgMatch.Groups["SD"].Value.Trim('[', ']', ' '));
                foreach(Capture kvTerm in sdMatch.Groups["term"].Captures)
                {
                    Capture sdKey = null;
                    foreach (Capture keyMatch in sdMatch.Groups["prefix"].Captures)
                    {
                        if (keyMatch.Index >= kvTerm.Index && keyMatch.Index <= kvTerm.Index + kvTerm.Length)
                        {
                            sdKey = keyMatch;
                            break;
                        }
                    }

                    if (sdKey == null)
                    {
                        if (!logmsg.Data.ContainsKey(kvTerm.Value))
                            logmsg.Data.Add(kvTerm.Value, null);
                        continue;
                    }

                    Capture sdValue = null;
                    foreach (Capture termStringMatch in sdMatch.Groups["termString"].Captures)
                    {
                        if (termStringMatch.Index >= kvTerm.Index && termStringMatch.Index <= kvTerm.Index + kvTerm.Length)
                        {
                            sdValue = termStringMatch;
                            break;
                        }
                    }

                    if (!logmsg.Data.ContainsKey(sdKey.Value))
                        logmsg.Data.Add(sdKey.Value, sdValue?.Value.Trim(' ', '"'));
                }
            }

            if (msgMatch.Groups["MSG"].Success)
                logmsg.Content = msgMatch.Groups["MSG"].Value;

            return true;
        }
    }
}
