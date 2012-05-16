﻿/*
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Aurora.Framework;
using Aurora.Framework.Capabilities;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;

namespace Aurora.Modules.Entities.PhysicsMaterials
{
    public class PhysicsMaterialsModule : INonSharedRegionModule
    {
        //private static readonly ILog MainConsole.Instance =
        //    LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private IScene m_scene;

        #region INonSharedRegionModule Members

        public void Initialise(IConfigSource pSource)
        {
        }

        public void AddRegion(IScene scene)
        {
            m_scene = scene;
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
        }

        public void RemoveRegion(IScene scene)
        {
            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
        }

        public void RegionLoaded(IScene scene)
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "PhysicsMaterialsModule"; }
        }

        public void Close()
        {
        }

        #endregion

        public void PostInitialise()
        {
        }

        public OSDMap RegisterCaps(UUID agentID, IHttpServer server)
        {
            OSDMap retVal = new OSDMap();
            retVal["GetObjectPhysicsData"] = CapsUtil.CreateCAPS("GetObjectPhysicsData", "");

#if (!ISWIN)
            server.AddStreamHandler(new RestHTTPHandler("POST", retVal["GetObjectPhysicsData"],
                                                      delegate(Hashtable m_dhttpMethod)
                                                      {
                                                          return GetObjectPhysicsData(agentID, m_dhttpMethod);
                                                      }));
#else
            server.AddStreamHandler(new RestHTTPHandler("POST", retVal["GetObjectPhysicsData"],
                                                        m_dhttpMethod => GetObjectPhysicsData(agentID, m_dhttpMethod)));
#endif
            return retVal;
        }

        private Hashtable GetObjectPhysicsData(UUID agentID, Hashtable mDhttpMethod)
        {
            OSDMap rm = (OSDMap) OSDParser.DeserializeLLSDXml((string) mDhttpMethod["requestbody"]);

            OSDArray keys = (OSDArray) rm["object_ids"];

            IEventQueueService eqs = m_scene.RequestModuleInterface<IEventQueueService>();
            if (eqs != null)
            {
#if (!ISWIN)
                List<ISceneChildEntity> list = new List<ISceneChildEntity>();
                foreach (OSD key in keys)
                    list.Add(m_scene.GetSceneObjectPart(key.AsUUID()));
                eqs.ObjectPhysicsProperties(list.ToArray(),
                                            agentID, m_scene.RegionInfo.RegionHandle);
#else
                eqs.ObjectPhysicsProperties(keys.Select(key => m_scene.GetSceneObjectPart(key.AsUUID())).ToArray(), agentID, m_scene.RegionInfo.RegionHandle);
#endif
            }
            //Send back data
            Hashtable responsedata = new Hashtable();
            responsedata["int_response_code"] = 200; //501; //410; //404;
            responsedata["content_type"] = "text/plain";
            responsedata["keepalive"] = false;
            responsedata["str_response_string"] = "";
            return responsedata;
        }
    }
}
