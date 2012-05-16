﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aurora.Framework;
using OpenSim.Services.LLLoginService;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using Nini.Config;
using System.Collections;

namespace OpenSim.Services.LLLoginService
{
    public class PasswordLoginModule : ILoginModule
    {
        protected IAuthenticationService m_AuthenticationService;
        public string Name
        {
            get { return GetType().Name; }
        }

        public void Initialize(ILoginService service, IConfigSource config, IRegistryCore registry)
        {
            m_AuthenticationService = registry.RequestModuleInterface<IAuthenticationService>();
        }

        public LoginResponse Login(Hashtable request, UserAccount account, IAgentInfo agentInfo, string authType, string password, out object data)
        {
            data = null;
            //
            // Authenticate this user
            //
            if (authType == "UserAccount")
            {
                password = password.StartsWith("$1$") ? password.Remove(0, 3) : Util.Md5Hash(password); //remove $1$
            }
            string token = m_AuthenticationService.Authenticate(account.PrincipalID, authType, password, 30);
            UUID secureSession = UUID.Zero;
            if ((token == string.Empty) || (token != string.Empty && !UUID.TryParse(token, out secureSession)))
                return LLFailedLoginResponse.AuthenticationProblem;
            data = secureSession;
            return null;
        }
    }
}
