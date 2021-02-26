﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Data.SqlClient
{
    /// <summary>Applies retry logic on an operation.</summary>
    internal class SqlRetryLogicProvider : SqlRetryLogicBaseProvider
    {
        private readonly string _typeName = nameof(SqlRetryLogicProvider);
        // keeps free RetryLogic objects
        private readonly ConcurrentBag<SqlRetryLogicBase> _retryLogicPool = new ConcurrentBag<SqlRetryLogicBase>();

        // safety switch for the preview version
        private const string EnableRetryLogicSwitch = "Switch.Microsoft.Data.SqlClient.EnableRetryLogic";
        private readonly bool enableRetryLogic = false;

        /// <summary>Creates an instance of this type.</summary>
        public SqlRetryLogicProvider(SqlRetryLogicBase retryLogic)
        {
            Debug.Assert(retryLogic != null, $"The '{nameof(retryLogic)}' cannot be null.");
            AppContext.TryGetSwitch(EnableRetryLogicSwitch, out enableRetryLogic);
            SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.SqlRetryLogicProvider|INFO> AppContext switch \"{1}\"={2}.", _typeName, EnableRetryLogicSwitch, enableRetryLogic);
            RetryLogic = retryLogic;
        }

        private SqlRetryLogicBase GetRetryLogic()
        {
            SqlRetryLogicBase retryLogic = null;
            if (enableRetryLogic && !_retryLogicPool.TryTake(out retryLogic))
            {
                retryLogic = RetryLogic.Clone() as SqlRetryLogicBase;
            }
            else
            {
                retryLogic?.Reset();
            }
            return retryLogic;
        }

        private void RetryLogicPoolAdd(SqlRetryLogicBase retryLogic)
        {
            if (retryLogic != null)
            {
                _retryLogicPool.Add(retryLogic);
            }
        }

        /// <summary>Executes a function and applies retry logic, if enabled.</summary>
        public override TResult Execute<TResult>(object sender, Func<TResult> function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            SqlRetryLogicBase retryLogic = null;
            var exceptions = new List<Exception>();
        retry:
            try
            {
                var result = function.Invoke();
                RetryLogicPoolAdd(retryLogic);
                return result;
            }
            catch (Exception e)
            {
                if (enableRetryLogic && RetryLogic.RetryCondition(sender) && RetryLogic.TransientPredicate(e))
                {
                    retryLogic = retryLogic ?? GetRetryLogic();
                    SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.Execute<TResult>|INFO> Found an action eligible for the retry policy (retried attempts = {1}).", _typeName, retryLogic.Current);
                    exceptions.Add(e);
                    if (retryLogic.TryNextInterval(out TimeSpan intervalTime))
                    {
                        // The retrying event raises on each retry.
                        ApplyRetryingEvent(sender, retryLogic, intervalTime, exceptions, e);

                        Thread.Sleep(intervalTime);
                        goto retry;
                    }
                    else
                    {
                        throw CreateException(exceptions, retryLogic);
                    }
                }
                else
                {
                    RetryLogicPoolAdd(retryLogic);
                    throw;
                }
            }
        }

        /// <summary>Executes a function and applies retry logic, if enabled.</summary>
        public override async Task<TResult> ExecuteAsync<TResult>(object sender, Func<Task<TResult>> function, CancellationToken cancellationToken = default)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            SqlRetryLogicBase retryLogic = null;
            var exceptions = new List<Exception>();
        retry:
            try
            {
                var result = await function.Invoke();
                RetryLogicPoolAdd(retryLogic);
                return result;
            }
            catch (Exception e)
            {
                if (enableRetryLogic && RetryLogic.RetryCondition(sender) && RetryLogic.TransientPredicate(e))
                {
                    retryLogic = retryLogic ?? GetRetryLogic();
                    SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.ExecuteAsync<TResult>|INFO> Found an action eligible for the retry policy (retried attempts = {1}).", _typeName, retryLogic.Current);
                    exceptions.Add(e);
                    if (retryLogic.TryNextInterval(out TimeSpan intervalTime))
                    {
                        // The retrying event raises on each retry.
                        ApplyRetryingEvent(sender, retryLogic, intervalTime, exceptions, e);

                        await Task.Delay(intervalTime, cancellationToken);
                        goto retry;
                    }
                    else
                    {
                        throw CreateException(exceptions, retryLogic);
                    }
                }
                else
                {
                    RetryLogicPoolAdd(retryLogic);
                    throw;
                }
            }
        }

        /// <summary>Executes a function and applies retry logic if, enabled.</summary>
        public override async Task ExecuteAsync(object sender, Func<Task> function, CancellationToken cancellationToken = default)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            SqlRetryLogicBase retryLogic = null;
            var exceptions = new List<Exception>();
        retry:
            try
            {
                await function.Invoke();
                RetryLogicPoolAdd(retryLogic);
            }
            catch (Exception e)
            {
                if (enableRetryLogic && RetryLogic.RetryCondition(sender) && RetryLogic.TransientPredicate(e))
                {
                    retryLogic = retryLogic ?? GetRetryLogic();
                    SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.ExecuteAsync|INFO> Found an action eligible for the retry policy (retried attempts = {1}).", _typeName, retryLogic.Current);
                    exceptions.Add(e);
                    if (retryLogic.TryNextInterval(out TimeSpan intervalTime))
                    {
                        // The retrying event raises on each retry.
                        ApplyRetryingEvent(sender, retryLogic, intervalTime, exceptions, e);

                        await Task.Delay(intervalTime, cancellationToken);
                        goto retry;
                    }
                    else
                    {
                        throw CreateException(exceptions, retryLogic);
                    }
                }
                else
                {
                    RetryLogicPoolAdd(retryLogic);
                    throw;
                }
            }
        }

        #region private methods

        private Exception CreateException(IList<Exception> exceptions, SqlRetryLogicBase retryLogic, bool manualCancellation = false)
        {
            string message;
            if (manualCancellation)
            {
                message = StringsHelper.GetString(Strings.SqlRetryLogic_RetryCanceled, retryLogic.Current);
            }
            else
            {
                SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.{1}|ERR|THROW> Exiting retry scope (exceeded the max allowed attempts = {2}).", _typeName, MethodBase.GetCurrentMethod().Name, retryLogic.NumberOfTries);
                message = StringsHelper.GetString(Strings.SqlRetryLogic_RetryExceeded, retryLogic.NumberOfTries);
            }

            _retryLogicPool.Add(retryLogic);
            return new AggregateException(message, exceptions);
        }

        private void ApplyRetryingEvent(object sender, SqlRetryLogicBase retryLogic, TimeSpan intervalTime, List<Exception> exceptions, Exception lastException)
        {
            if (Retrying != null)
            {
                var retryEventArgs = new SqlRetryingEventArgs(retryLogic.Current, intervalTime, exceptions);
                SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.{1}|INFO> Running the retrying event.", _typeName, MethodBase.GetCurrentMethod().Name);
                Retrying?.Invoke(sender, retryEventArgs);
                if (retryEventArgs.Cancel)
                {
                    SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.{1}|INFO> Retry attempt cancelled (current retry number = {2}).", _typeName, MethodBase.GetCurrentMethod().Name, retryLogic.Current);
                    throw CreateException(exceptions, retryLogic, true);
                }
            }
            SqlClientEventSource.Log.TryTraceEvent("<sc.{0}.{1}|INFO> Wait '{2}' and run the action for retry number {3}.", _typeName, MethodBase.GetCurrentMethod().Name, intervalTime, retryLogic.Current);
        }
        #endregion
    }
}
