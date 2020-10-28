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
using System.Text;

namespace IDNT.AppBase.Network.Protocol.Syslog
{
    public enum SyslogFacility
    {
        /// <summary>
        /// Kernel messages
        /// </summary>
        Kern = 0,
        /// <summary>
        /// General userland messages
        /// </summary>
        User,
        /// <summary>
        /// E-Mail system
        /// </summary>
        Mail,
        /// <summary>
        /// Daemon (server process) messages
        /// </summary>
        Daemon,
        /// <summary>
        /// Authentication or security messages
        /// </summary>
        Auth,
        /// <summary>
        /// Internal syslog messages
        /// </summary>
        Syslog,
        /// <summary>
        /// Printer messages
        /// </summary>
        LPR,
        /// <summary>
        /// Usenet news
        /// </summary>
        News,
        /// <summary>
        /// UUCP messages
        /// </summary>
        UUCP,
        /// <summary>
        /// Cron messages
        /// </summary>
        Cron,
        /// <summary>
        /// Private authentication messages
        /// </summary>
        AuthPriv,
        /// <summary>
        /// FTP messages
        /// </summary>
        FTP, 
        NTP,
        Audit, 
        Audit2, 
        CRON2, 
        Local0, 
        Local1, 
        Local2, 
        Local3, 
        Local4, 
        Local5, 
        Local6, 
        Local7
    };
}
