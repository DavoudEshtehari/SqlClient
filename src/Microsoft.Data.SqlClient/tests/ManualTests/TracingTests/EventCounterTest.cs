// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public sealed class EventCounterTestFixture: IDisposable
    {
        internal CollectingEventCounterListener Listener { get; set; }

        public void EnableListeners() => Listener ??= new CollectingEventCounterListener();

        public void Dispose() => Listener?.Dispose();
    }

    /// <summary>
    /// This unit test is just valid for .NetCore 3.0 and above
    /// </summary>
    public class EventCounterTest: IClassFixture<EventCounterTestFixture>
    {
        private readonly EventCounterTestFixture _fixture;
        private const string ActiveHardConnects = "active-hard-connections";
        private const string ActiveSoftConnects = "active-soft-connects";
        private const string NumberOfNonPooledConnections = "number-of-non-pooled-connections";
        private const string NumberOfPooledConnections = "number-of-pooled-connections";
        private const string NumberOfActiveConnectionPoolGroups = "number-of-active-connection-pool-groups";
        private const string NumberOfActiveConnectionPools = "number-of-active-connection-pools";
        private const string NumberOfActiveConnections = "number-of-active-connections";
        private const string NumberOfFreeConnections = "number-of-free-connections";
        private const string NumberOfStasisConnections = "number-of-stasis-connections";

        public EventCounterTest(EventCounterTestFixture fixture)
        {
            _fixture = fixture;
            ClearConnectionPools();
            fixture.EnableListeners();
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public void EventCounterTestAll()
        {
            var stringBuilder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString)
            {
                Pooling = true, MaxPoolSize = 20
            };

            OpenConnections(stringBuilder.ConnectionString);
            stringBuilder.Pooling = false;
            OpenConnections(stringBuilder.ConnectionString);

            Thread.Sleep(3000);
            _fixture.Listener.WaitForStatsUpdated();

            //there are 16 counters total - all of them must have been collected
            Assert.Equal(16, _fixture.Listener.EventCounters.Count);
        }

        private void OpenConnections(string cnnString)
        {
            List<Task> tasks = new List<Task>();

            Enumerable.Range(1, 100).ToList().ForEach(i =>
            {
                SqlConnection cnn = new SqlConnection(cnnString);
                cnn.Open();
                int x = i;
                tasks.Add(Task.Run(() =>
                {
                    Thread.Sleep(x);
                    cnn.Close();
                }));
            });
            Task.WhenAll(tasks).Wait();
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public void EventCounter_HardConnectionsCounters_Functional()
        {
            //create a non-pooled connection
            var stringBuilder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString) {Pooling = false};

            using var conn = new SqlConnection(stringBuilder.ToString());
            _fixture.Listener.WaitForStatsUpdated();

            //initially we have no open physical connections
            Assert.Equal(0, _fixture.Listener.EventCounters[ActiveHardConnects]);
            Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfNonPooledConnections]);

            conn.Open();
            _fixture.Listener.WaitForStatsUpdated();

            //when the connection gets opened, the real physical connection appears
            Assert.Equal(1, _fixture.Listener.EventCounters[ActiveHardConnects]);
            Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfNonPooledConnections]);

            conn.Close();
            _fixture.Listener.WaitForStatsUpdated();

            //when the connection gets closed, the real physical connection is also closed
            Assert.Equal(0, _fixture.Listener.EventCounters[ActiveHardConnects]);
            Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfNonPooledConnections]);
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public void EventCounter_SoftConnectionsCounters_Functional()
        {
            //create a pooled connection
            var stringBuilder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString) {Pooling = true};

            using (var conn = new SqlConnection(stringBuilder.ToString()))
            {
                _fixture.Listener.WaitForStatsUpdated();

                //initially we have no open physical connections
                Assert.Equal(0, _fixture.Listener.EventCounters[ActiveHardConnects]);
                Assert.Equal(0, _fixture.Listener.EventCounters[ActiveSoftConnects]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfPooledConnections]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfActiveConnectionPoolGroups]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfActiveConnectionPools]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfActiveConnections]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfFreeConnections]);

                conn.Open();
                _fixture.Listener.WaitForStatsUpdated();

                //when the connection gets opened, the real physical connection appears
                //and the appropriate pooling infrastructure gets deployed
                Assert.Equal(1, _fixture.Listener.EventCounters[ActiveHardConnects]);
                Assert.Equal(1, _fixture.Listener.EventCounters[ActiveSoftConnects]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfPooledConnections]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPoolGroups]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPools]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnections]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfFreeConnections]);

                conn.Close();
                _fixture.Listener.WaitForStatsUpdated();

                //when the connection gets closed, the real physical connection gets returned to the pool
                Assert.Equal(1, _fixture.Listener.EventCounters[ActiveHardConnects]);
                Assert.Equal(0, _fixture.Listener.EventCounters[ActiveSoftConnects]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfPooledConnections]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPoolGroups]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPools]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfActiveConnections]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfFreeConnections]);
            }

            using (var conn2 = new SqlConnection(stringBuilder.ToString()))
            {
                conn2.Open();
                _fixture.Listener.WaitForStatsUpdated();

                //the next open connection will reuse the underlying physical connection
                Assert.Equal(1, _fixture.Listener.EventCounters[ActiveHardConnects]);
                Assert.Equal(1, _fixture.Listener.EventCounters[ActiveSoftConnects]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfPooledConnections]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPoolGroups]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnectionPools]);
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfActiveConnections]);
                Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfFreeConnections]);
            }
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public void EventCounter_StasisCounters_Functional()
        {
            var stringBuilder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString) {Pooling = false};

            using (var conn = new SqlConnection(stringBuilder.ToString()))
            using (new TransactionScope())
            {
                conn.Open();
                conn.EnlistTransaction(System.Transactions.Transaction.Current);
                conn.Close();

                _fixture.Listener.WaitForStatsUpdated();

                //when the connection gets closed, but the ambient transaction is still in prigress
                //the physical connection gets in stasis, until the transaction ends
                Assert.Equal(1, _fixture.Listener.EventCounters[NumberOfStasisConnections]);
            }

            //when the transaction finally ends, the physical connection is returned from stasis
            _fixture.Listener.WaitForStatsUpdated();
            Assert.Equal(0, _fixture.Listener.EventCounters[NumberOfStasisConnections]);
        }

        private void ClearConnectionPools()
        {
            FieldInfo connectionFactoryField =
                typeof(SqlConnection).GetField("s_connectionFactory", BindingFlags.Static | BindingFlags.NonPublic);
            Debug.Assert(connectionFactoryField != null);

            MethodInfo pruneConnectionPoolGroupsMethod =
                connectionFactoryField.FieldType.GetMethod("PruneConnectionPoolGroups",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            Debug.Assert(pruneConnectionPoolGroupsMethod != null);
            pruneConnectionPoolGroupsMethod.Invoke(connectionFactoryField.GetValue(null), new []{(object)null});

            MethodInfo clearAllPoolsMethod =
                connectionFactoryField.FieldType.GetMethod("ClearAllPools",
                    BindingFlags.Public | BindingFlags.Instance);
            Debug.Assert(clearAllPoolsMethod != null);
            clearAllPoolsMethod.Invoke(connectionFactoryField.GetValue(null), Array.Empty<object>());
        }
    }

    internal class CollectingEventCounterListener : EventListener
    {
        private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private const byte _eventCounterIntervalSec = 1;
        private const int _eventDeliveryWaitMSec = _eventCounterIntervalSec * 2 * 1000;
        private const int _maxEventDeliveryWaitMSec = _eventCounterIntervalSec * 5 * 1000;
        private IList<EventSource> _enabledSources = new List<EventSource>();

        public Dictionary<string, double> EventCounters { get; } = new Dictionary<string, double>();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.Equals("Microsoft.Data.SqlClient.EventSource"))
            {
                // define time interval 1 second
                // without defining this parameter event counters will not enabled
                // enable for the None keyword
                var options =
                    new Dictionary<string, string> {{"EventCounterIntervalSec", _eventCounterIntervalSec.ToString()}};
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.None, options);
                _enabledSources.Add(eventSource);
            }
        }

        public override void Dispose()
        {
            foreach (EventSource eventSource in _enabledSources)
                DisableEvents(eventSource);
            base.Dispose();
        }

        public void WaitForStatsUpdated()
        {
            _stopwatch.Start();
            Assert.True(_resetEvent.WaitOne(_maxEventDeliveryWaitMSec), "The test failed by timeout");
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!string.Equals(eventData.EventName, "EventCounters", StringComparison.InvariantCultureIgnoreCase))
                return;

            for (int i = 0; i < eventData.Payload!.Count; ++i)
                if (eventData.Payload[i] is IDictionary<string, object> eventPayload)
                    if (TryGetRelevantMetric(eventPayload, out object metric, out object value))
                        EventCounters[(string)metric] = (double)value;

            if (_stopwatch.ElapsedMilliseconds > _eventDeliveryWaitMSec)
            {
                _stopwatch.Reset();
                _resetEvent.Set();
            }
        }

        private static bool TryGetRelevantMetric(
            IDictionary<string, object> eventPayload, out object metric, out object value)
        {
            byte propertiesFound = 0;

            if (eventPayload.TryGetValue("Name", out metric))
                propertiesFound++;

            if (eventPayload.TryGetValue("Mean", out value) || eventPayload.TryGetValue("Increment", out value))
                propertiesFound++;

            const byte propertiesRequired = 2;
            return propertiesFound == propertiesRequired;
        }
    }
}
