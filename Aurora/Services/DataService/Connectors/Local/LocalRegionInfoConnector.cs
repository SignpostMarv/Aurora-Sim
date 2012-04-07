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
using System.Linq;
using Aurora.Framework;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Aurora.Services.DataService
{
    public class LocalRegionInfoConnector : IRegionInfoConnector
    {
        private IGenericData GD = null;
        private IRegistryCore m_registry = null;
        private const string m_regionSettingsRealm = "regionsettings";

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore reg, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("RegionInfoConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;
                m_registry = reg;

                if (source.Configs[Name] != null)
                    defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase (defaultConnectionString, "Generics", source.Configs["AuroraConnectors"].GetBoolean ("ValidateTables", true));
                GD.ConnectToDatabase (defaultConnectionString, "RegionInfo", source.Configs["AuroraConnectors"].GetBoolean ("ValidateTables", true));
                
                DataManager.DataManager.RegisterPlugin(this);
            }
        }

        public string Name
        {
            get { return "IRegionInfoConnector"; }
        }

        public void Dispose()
        {
        }

        public void UpdateRegionInfo(RegionInfo region)
        {
            RegionInfo oldRegion = GetRegionInfo(region.RegionID);
            if (oldRegion != null)
            {
                m_registry.RequestModuleInterface<ISimulationBase>().EventManager.FireGenericEventHandler("RegionInfoChanged", new[] 
                {
                    oldRegion,
                    region
                });
            }
            Dictionary<string, object> row = new Dictionary<string, object>(4);
            row["RegionID"] = region.RegionID;
            row["RegionName"] = region.RegionName.MySqlEscape(50);
            row["RegionInfo"] = OSDParser.SerializeJsonString(region.PackRegionInfoData(true));
            row["DisableD"] = region.Disabled ? 1 : 0;
            GD.Replace("simulator", row);
        }

        public void Delete(RegionInfo region)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["RegionID"] = region.RegionID;
            GD.Delete("simulator", filter);
        }

        public RegionInfo[] GetRegionInfos(bool nonDisabledOnly)
        {
            QueryFilter filter = new QueryFilter();
            if (nonDisabledOnly)
            {
                filter.andFilters["Disabled"] = 0;
            }
                
            List<string> RetVal = GD.Query(new string[1] { "RegionInfo" }, "simulator", filter, null, null, null);

            if (RetVal.Count == 0)
            {
                return new RegionInfo[0]{};
            }

            List<RegionInfo> Infos = new List<RegionInfo>();
            RegionInfo replyData;
            foreach (string t in RetVal)
            {
                replyData = new RegionInfo();
                replyData.UnpackRegionInfoData((OSDMap)OSDParser.DeserializeJson(t));
                Infos.Add(replyData);
            }
            //Sort by startup number
            Infos.Sort(RegionInfoStartupSorter);
            return Infos.ToArray();
        }

        private int RegionInfoStartupSorter(RegionInfo A, RegionInfo B)
        {
            return A.NumberStartup.CompareTo(B.NumberStartup);
        }

        public RegionInfo GetRegionInfo (UUID regionID)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["RegionID"] = regionID;
            List<string> RetVal = GD.Query(new string[1] { "RegionInfo" }, "simulator", filter, null, null, null);

            if (RetVal.Count == 0)
            {
                return null;
            }

            RegionInfo replyData = new RegionInfo();
            replyData.UnpackRegionInfoData((OSDMap)OSDParser.DeserializeJson(RetVal[0]));
            return replyData;
        }

        public RegionInfo GetRegionInfo (string regionName)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["RegionName"] = regionName.MySqlEscape(50);
            List<string> RetVal = GD.Query(new string[1] { "RegionInfo" }, "simulator", filter, null, null, null);

            if (RetVal.Count == 0)
            {
                return null;
            }

            RegionInfo replyData = new RegionInfo();
            replyData.UnpackRegionInfoData((OSDMap)OSDParser.DeserializeJson(RetVal[0]));
            return replyData;
        }

        public Dictionary<float, RegionLightShareData> LoadRegionWindlightSettings(UUID regionUUID)
        {
            Dictionary<float, RegionLightShareData> RetVal = new Dictionary<float, RegionLightShareData>();
            RegionLightShareData RWLD = new RegionLightShareData();
            List<RegionLightShareData> RWLDs = GenericUtils.GetGenerics<RegionLightShareData>(regionUUID, "RegionWindLightData", GD);
            foreach (RegionLightShareData lsd in RWLDs)
            {
                if(!RetVal.ContainsKey(lsd.minEffectiveAltitude))
                    RetVal.Add(lsd.minEffectiveAltitude, lsd);
            }
            return RetVal;
        }

        public void StoreRegionWindlightSettings(UUID RegionID, UUID ID, RegionLightShareData map)
        {
            GenericUtils.AddGeneric(RegionID, "RegionWindLightData", ID.ToString(), map.ToOSD(), GD);
        }

        #region Region Settings

        public RegionSettings LoadRegionSettings (UUID regionUUID)
        {
            RegionSettings settings = new RegionSettings ();

            QueryFilter filter = new QueryFilter();
            filter.andFilters["regionUUID"] = regionUUID;

            List<string> query = GD.Query(new string[1] { "*" }, m_regionSettingsRealm, filter, null, null, null);

            if (query.Count % 45 != 0)
            {
                settings.RegionUUID = regionUUID;
                StoreRegionSettings (settings);
            }
            else
            {
                for (int i = 0; i < query.Count; i += 45)
                {
                    settings.RegionUUID = UUID.Parse (query[i]);
                    settings.BlockTerraform = int.Parse (query[i + 1]) == 1;
                    settings.BlockFly = int.Parse (query[i + 2]) == 1;
                    settings.AllowDamage = int.Parse (query[i + 3]) == 1;
                    settings.RestrictPushing = int.Parse (query[i + 4]) == 1;
                    settings.AllowLandResell = int.Parse (query[i + 5]) == 1;
                    settings.AllowLandJoinDivide = int.Parse (query[i + 6]) == 1;
                    settings.BlockShowInSearch = int.Parse (query[i + 7]) == 1;
                    settings.AgentLimit = int.Parse (query[i + 8]);
                    settings.ObjectBonus = double.Parse (query[i + 9]);
                    settings.Maturity = int.Parse (query[i + 10]);
                    settings.DisableScripts = int.Parse (query[i + 11]) == 1;
                    settings.DisableCollisions = int.Parse (query[i + 12]) == 1;
                    settings.DisablePhysics = int.Parse (query[i + 13]) == 1;
                    settings.TerrainTexture1 = UUID.Parse (query[i + 14]);
                    settings.TerrainTexture2 = UUID.Parse (query[i + 15]);
                    settings.TerrainTexture3 = UUID.Parse (query[i + 16]);
                    settings.TerrainTexture4 = UUID.Parse (query[i + 17]);
                    settings.Elevation1NW = double.Parse (query[i + 18]);
                    settings.Elevation2NW = double.Parse (query[i + 19]);
                    settings.Elevation1NE = double.Parse (query[i + 20]);
                    settings.Elevation2NE = double.Parse (query[i + 21]);
                    settings.Elevation1SE = double.Parse (query[i + 22]);
                    settings.Elevation2SE = double.Parse (query[i + 23]);
                    settings.Elevation1SW = double.Parse (query[i + 24]);
                    settings.Elevation2SW = double.Parse (query[i + 25]);
                    settings.WaterHeight = double.Parse (query[i + 26]);
                    settings.TerrainRaiseLimit = double.Parse (query[i + 27]);
                    settings.TerrainLowerLimit = double.Parse (query[i + 28]);
                    settings.UseEstateSun = int.Parse (query[i + 29]) == 1;
                    settings.FixedSun = int.Parse (query[i + 30]) == 1;
                    settings.SunPosition = double.Parse (query[i + 31]);
                    settings.Covenant = UUID.Parse (query[i + 32]);
                    settings.Sandbox = int.Parse(query[i + 33]) == 1;
                    settings.SunVector = new Vector3 (
                        float.Parse (query[i + 34]),
                        float.Parse (query[i + 35]),
                        float.Parse (query[i + 36])
                    );
                    settings.LoadedCreationID = query[i + 37];
                    settings.LoadedCreationDateTime = int.Parse (query[i + 38]);
                    settings.TerrainMapImageID = UUID.Parse (query[i + 39]);
                    settings.TerrainImageID = UUID.Parse (query[i + 40]);
                    settings.MinimumAge = int.Parse(query[i + 41]);
                    settings.CovenantLastUpdated = int.Parse(query[i + 42]);
                    OSD o = OSDParser.DeserializeJson(query[i + 43]);
                    if (o.Type == OSDType.Map)
                    {
                        settings.Generic = (OSDMap)o;
                    }

                    settings.TerrainMapLastRegenerated = Util.ToDateTime(int.Parse(query[i + 44]));
                }
            }
            settings.OnSave += StoreRegionSettings;
            return settings;
        }

        public void StoreRegionSettings (RegionSettings rs)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["regionUUID"] = rs.RegionUUID;
            //Delete the original
            GD.Delete (m_regionSettingsRealm, filter);
            //Now replace with the new
            GD.Insert (m_regionSettingsRealm, new object[] {
                rs.RegionUUID,
                rs.BlockTerraform ? 1 : 0,
                rs.BlockFly ? 1 : 0,
                rs.AllowDamage ? 1 : 0,
                rs.RestrictPushing ? 1 : 0,
                rs.AllowLandResell ? 1 : 0,
                rs.AllowLandJoinDivide ? 1 : 0,
                rs.BlockShowInSearch ? 1 : 0,
                rs.AgentLimit, rs.ObjectBonus,
                rs.Maturity,
                rs.DisableScripts ? 1 : 0,
                rs.DisableCollisions ? 1 : 0,
                rs.DisablePhysics ? 1 : 0,
                rs.TerrainTexture1,
                rs.TerrainTexture2,
                rs.TerrainTexture3,
                rs.TerrainTexture4,
                rs.Elevation1NW,
                rs.Elevation2NW,
                rs.Elevation1NE,
                rs.Elevation2NE,
                rs.Elevation1SE,
                rs.Elevation2SE,
                rs.Elevation1SW,
                rs.Elevation2SW,
                rs.WaterHeight,
                rs.TerrainRaiseLimit,
                rs.TerrainLowerLimit,
                rs.UseEstateSun ? 1 : 0,
                rs.FixedSun ? 1 : 0,
                rs.SunPosition,
                rs.Covenant,
                rs.Sandbox ? 1 : 0,
                rs.SunVector.X,
                rs.SunVector.Y,
                rs.SunVector.Z,
                (rs.LoadedCreationID ?? ""),
                rs.LoadedCreationDateTime,
                rs.TerrainMapImageID,
                rs.TerrainImageID,
                rs.MinimumAge,
                rs.CovenantLastUpdated,
                OSDParser.SerializeJsonString(rs.Generic),
                Util.ToUnixTime(rs.TerrainMapLastRegenerated)
            });
        }

        #endregion
    }
}
