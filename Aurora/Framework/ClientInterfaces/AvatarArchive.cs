/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using OpenMetaverse.StructuredData;

namespace Aurora.Framework
{
    public class AvatarArchive : IDataTransferable
    {
        /// <summary>
        ///   XML of the archive
        /// </summary>
        public string ArchiveXML;

        /// <summary>
        ///   1 or 0 if its public
        /// </summary>
        public int IsPublic;

        /// <summary>
        ///   Name of the archive
        /// </summary>
        public string Name;

        /// <summary>
        ///   uuid of a text that shows off this archive
        /// </summary>
        public string Snapshot;

        public override OSDMap ToOSD()
        {
            OSDMap map = new OSDMap();

            map["ArchiveXML"] = ArchiveXML;
            map["IsPublic"] = IsPublic;
            map["Name"] = Name;
            map["Snapshot"] = Snapshot;

            return map;
        }

        public override void FromOSD(OSDMap map)
        {
            ArchiveXML = map["ArchiveXML"];
            IsPublic = map["IsPublic"];
            Name = map["Name"];
            Snapshot = map["Snapshot"];
        }
    }
}
