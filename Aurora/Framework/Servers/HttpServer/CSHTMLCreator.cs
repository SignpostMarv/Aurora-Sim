﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace Aurora.Framework
{
    public delegate string HTTPReturned(Dictionary<string, string> variables);
    public class CSHTMLCreator
    {
        public static string AddHTMLPage(string html, string urlToAppend, string methodName, Dictionary<string, string> variables, HTTPReturned eventDelegate)
        {
            string secret = Util.RandomClass.Next(0, int.MaxValue).ToString();
            string secret2 = Util.RandomClass.Next(0, int.MaxValue).ToString();
            string navUrl = MainServer.Instance.ServerURI +
                (urlToAppend == "" ? "" : "/") + urlToAppend + "/index.php?method=" + methodName + secret2;
            string url = MainServer.Instance.ServerURI +
                (urlToAppend == "" ? "" : "/") + urlToAppend + "/index.php?method=" + methodName + secret;
            MainServer.Instance.RemoveHTTPHandler(null, methodName + secret);
            MainServer.Instance.RemoveHTTPHandler(null, methodName + secret2);
            variables["url"] = url;
            MainServer.Instance.AddHTTPHandler(methodName + secret2, delegate(Hashtable t)
            {
                MainServer.Instance.RemoveHTTPHandler(null, methodName + secret2);
                return SetUpWebpage(t, url, html, variables);
            });
            MainServer.Instance.AddHTTPHandler(methodName + secret, delegate(Hashtable t)
            {
                MainServer.Instance.RemoveHTTPHandler(null, methodName + secret);
                return HandleResponse(t, urlToAppend, variables, eventDelegate);
            });
            return navUrl;
        }

        private static Hashtable HandleResponse(Hashtable request, string urlToAppend, Dictionary<string, string> variables, HTTPReturned eventHandler)
        {
            Uri myUri = new Uri("http://localhost/index.php?" + request["body"]);
            Dictionary<string, string> newVars = new Dictionary<string, string>();
            foreach (string key in variables.Keys)
            {
                newVars[key] = HttpUtility.ParseQueryString(myUri.Query).Get(key);
            }
            string url = eventHandler(newVars);

            Hashtable reply = new Hashtable();
            string html = "<html>" +
                (url == "" ? "" :
("<head>" +
"<meta http-equiv=\"REFRESH\" content=\"0;url=" + url + "\"></HEAD>")) +
"</HTML>";
            reply["str_response_string"] = html;
            reply["int_response_code"] = 200;
            reply["content_type"] = "text/html";

            return reply;
        }

        private static Hashtable SetUpWebpage(Hashtable t, string url, string html, Dictionary<string, string> vars)
        {
            Hashtable reply = new Hashtable();
            reply["str_response_string"] = BuildHTML(html, vars);
            reply["int_response_code"] = 200;
            reply["content_type"] = "text/html";

            return reply;
        }

        private static string BuildHTML(string html, Dictionary<string, string> vars)
        {
            foreach (KeyValuePair<string, string> kvp in vars)
            {
                html = html.Replace("{" + kvp.Key + "}", kvp.Value);
            }
            return html;
        }
    }
}
