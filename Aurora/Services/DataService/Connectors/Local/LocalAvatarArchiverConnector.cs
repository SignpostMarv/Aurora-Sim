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

using System.Collections.Generic;
using System.Data;
using Aurora.Framework;
using Nini.Config;
using OpenMetaverse;

namespace Aurora.Services.DataService
{
    //This will always be local, as this is only used by the grid server.
    //The region server should not be using this class.
    public class LocalAvatarArchiverConnector : IAvatarArchiverConnector
    {
        private IGenericData GD;

        #region IAvatarArchiverConnector Members

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase,
                               string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("AvatarArchiverConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                if (source.Configs[Name] != null)
                {
                    defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);
                }

                GD.ConnectToDatabase(defaultConnectionString, "AvatarArchive",
                                     source.Configs["AuroraConnectors"].GetBoolean("ValidateTables", true));
                DataManager.DataManager.RegisterPlugin(this);
            }
        }

        public string Name
        {
            get { return "IAvatarArchiverConnector"; }
        }

        public AvatarArchive GetAvatarArchive(string Name)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["Name"] = Name;
            List<string> RetVal = GD.Query(new string[] { "*" }, "avatararchives", filter, null, null, null);

            return (RetVal.Count == 0) ?  null :  new AvatarArchive {
                Name = RetVal[0],
                ArchiveXML = RetVal[1]
            };
        }

        /// <summary>
        ///   Returns a list object of AvatarArchives. This is being used for WebUI
        /// </summary>
        /// <param name = "Public"></param>
        /// <returns></returns>
        public List<AvatarArchive> GetAvatarArchives(bool isPublic)
        {
            List<AvatarArchive> returnValue = new List<AvatarArchive>();

            QueryFilter filter = new QueryFilter();
            filter.andFilters["IsPublic"] = isPublic ? 1 : 0;

            List<string> query = GD.Query(new string[3]{
                "Name",
                "Snapshot",
                "IsPublic"
            }, "avatararchives", filter, null, null, null);

            if (query.Count % 3 == 0)
            {
                for (int i = 0; i < query.Count; i += 3)
                {
                    returnValue.Add(new AvatarArchive
                    {
                        Name = query[i],
                        Snapshot = query[i+1],
                        IsPublic = int.Parse(query[i+2])
                    });
                }
            }

            return returnValue;
        }

        public void SaveAvatarArchive(AvatarArchive archive)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["Name"] = archive.Name.MySqlEscape();
            List<string> Check = GD.Query(new string[] { "Name" }, "avatararchives", filter, null, null, null);
            if (Check.Count == 0)
            {
                GD.Insert("avatararchives", new object[]{
                    archive.Name.MySqlEscape(),
                    archive.ArchiveXML,
                    archive.Snapshot.MySqlEscape(),
                    archive.IsPublic
                });
            }
            else
            {
                Dictionary<string, object> values = new Dictionary<string, object>(1);
                values["Archive"] = archive.ArchiveXML;

                GD.Update("avatararchives", values, null, filter, null, null);
            }
        }

        #endregion

        public void Dispose()
        {
        }
    }
}