﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Reflection;

namespace Microsoft.Data.SqlClient
{
    /// <summary>
    /// Configurable retry logic manager
    /// </summary>
    internal partial class SqlConfigurableRetryLogicManager : SqlConfigurableRetryLogicManagerBase
    {
        /// <summary>
        /// Performs a case-sensitive search to resolve the specified type name.
        /// </summary>
        /// <param name="fullyQualifiedName"></param>
        /// <returns>Resolved type if could resolve the type; otherwise, the `SqlConfigurableRetryFactory` type.</returns>
        private static Type LoadType(string fullyQualifiedName)
        {
            string methodName = MethodBase.GetCurrentMethod().Name;

            var result = Type.GetType(fullyQualifiedName);
            SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.{1}|INFO> The '{2}' type is resolved."
                                                   , s_typeName, methodName, result?.FullName ?? NullConst);
            return result != null ? result : typeof(SqlConfigurableRetryFactory);
        }
    }
}
