﻿/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Apache.Ignite.AspNet.Impl
{
    using System;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Diagnostics;
    using System.Globalization;
    using Apache.Ignite.Core;
    using Apache.Ignite.Core.Cache;
    using Apache.Ignite.Core.Common;
    using Apache.Ignite.Core.Log;

    /// <summary>
    /// Config utils.
    /// </summary>
    internal static class ConfigUtil
    {
        /** */
        public const string GridName = "gridName";

        /** */
        public const string CacheName = "cacheName";

        /** */
        public const string IgniteConfigurationSectionName = "igniteConfigurationSectionName";

        /// <summary>
        /// Initializes the cache from configuration.
        /// </summary>
        public static ICache<TK, TV> InitializeCache<TK, TV>(NameValueCollection config, Type callerType)
        {
            Debug.Assert(config != null);
            Debug.Assert(callerType != null);

            var gridName = config[GridName];
            var cacheName = config[CacheName];
            var cfgSection = config[IgniteConfigurationSectionName];

            try
            {
                var grid = cfgSection != null
                    ? StartFromApplicationConfiguration(cfgSection)
                    : Ignition.GetIgnite(gridName);

                grid.Logger.Info("Initializing {0} with cache '{1}'", callerType, cacheName);

                return grid.GetOrCreateCache<TK, TV>(cacheName);
            }
            catch (Exception ex)
            {
                throw new IgniteException(string.Format(CultureInfo.InvariantCulture,
                    "Failed to initialize {0}: {1}", callerType, ex), ex);
            }

        }

        /// <summary>
        /// Starts Ignite from application configuration.
        /// </summary>
        private static IIgnite StartFromApplicationConfiguration(string sectionName)
        {
            var section = ConfigurationManager.GetSection(sectionName) as IgniteConfigurationSection;

            if (section == null)
                throw new ConfigurationErrorsException(string.Format(CultureInfo.InvariantCulture,
                    "Could not find {0} with name '{1}'", typeof(IgniteConfigurationSection).Name, sectionName));

            var config = section.IgniteConfiguration;

            if (string.IsNullOrWhiteSpace(config.IgniteHome))
            {
                // IgniteHome not set by user: populate from default directory
                config = new IgniteConfiguration(config) { IgniteHome = IgniteWebUtils.GetWebIgniteHome() };
            }

            return Ignition.Start(config);
        }

    }
}
