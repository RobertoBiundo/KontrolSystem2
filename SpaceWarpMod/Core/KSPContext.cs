﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using KontrolSystem.KSP.Runtime;
using KontrolSystem.KSP.Runtime.KSPConsole;
using KontrolSystem.KSP.Runtime.KSPGame;
using KontrolSystem.KSP.Runtime.KSPOrbit;
using KontrolSystem.KSP.Runtime.KSPResource;
using KontrolSystem.KSP.Runtime.KSPTelemetry;
using KontrolSystem.SpaceWarpMod.UI;
using KontrolSystem.TO2.Runtime;
using KSP.Game;
using KSP.Sim.impl;
using KSP.Sim.State;
using UnityEngine;

namespace KontrolSystem.SpaceWarpMod.Core {
    internal class AutopilotHooks {
        private readonly IKSPContext context;
        internal readonly List<IKSPAutopilot> autopilots = new List<IKSPAutopilot>();

        internal AutopilotHooks(IKSPContext context) => this.context = context;

        internal void Add(IKSPAutopilot autopilot) {
            if (!autopilots.Contains(autopilot)) autopilots.Add(autopilot);
        }

        internal bool Remove(IKSPAutopilot autopilot) => autopilots.Remove(autopilot);

        internal bool IsEmpty => autopilots.Count == 0;

        internal bool TryFindAutopilot<T>(out T autopilot) where T : IKSPAutopilot {
            foreach (var item in autopilots) {
                if (item is T t) {
                    autopilot = t;
                    return true;
                }
            }

            autopilot = default;
            return false;
        }

        internal void RunAutopilots(ref FlightCtrlState state, float deltaTime) {
            try {
                ContextHolder.CurrentContext.Value = context;
                foreach (IKSPAutopilot autopilot in autopilots)
                    autopilot.UpdateAutopilot(ref state, deltaTime);
            } finally {
                ContextHolder.CurrentContext.Value = null;
            }
        }
    }
    public class KSPContext : IKSPContext {
        internal static readonly int MAX_CALL_STACK = 100;

        private readonly GameInstance gameInstance;
        private readonly KSPConsoleBuffer consoleBuffer;
        private readonly TimeSeriesCollection timeSeriesCollection;
        private object nextYield;
        private Action onNextYieldOnce;
        private readonly Stopwatch timeStopwatch;
        private readonly long timeoutMillis;
        internal readonly List<IMarker> markers;
        internal readonly List<KSPResourceModule.ResourceTransfer> resourceTransfers;
        private readonly Dictionary<VesselComponent, AutopilotHooks> autopilotHooks;
        private readonly List<BackgroundKSPContext> childContexts;
        private int stackCallCount = 0;

        public KSPContext(GameInstance gameInstance, KSPConsoleBuffer consoleBuffer, TimeSeriesCollection timeSeriesCollection) {
            this.gameInstance = gameInstance;
            this.consoleBuffer = consoleBuffer;
            this.timeSeriesCollection = timeSeriesCollection;
            markers = new List<IMarker>();
            resourceTransfers = new List<KSPResourceModule.ResourceTransfer>();
            autopilotHooks = new Dictionary<VesselComponent, AutopilotHooks>();
            nextYield = new WaitForFixedUpdate();
            childContexts = new List<BackgroundKSPContext>();
            timeStopwatch = Stopwatch.StartNew();
            timeoutMillis = 100;
        }


        public bool IsBackground => false;
        public ITO2Logger Logger => LoggerAdapter.Instance;

        public void CheckTimeout() {
            long elapsed = timeStopwatch.ElapsedMilliseconds;
            if (elapsed >= timeoutMillis)
                throw new YieldTimeoutException(elapsed);
        }

        public void ResetTimeout() {
            if (onNextYieldOnce != null) {
                onNextYieldOnce();
                onNextYieldOnce = null;
            }
            timeStopwatch.Reset();
            timeStopwatch.Start();
        }


        public void FunctionEnter(string name, object[] arguments) {
            if (Interlocked.Increment(ref stackCallCount) > MAX_CALL_STACK) {
                throw new StackOverflowException($"Exceed stack count: {MAX_CALL_STACK}");
            }
        }

        public void FunctionLeave() {
            Interlocked.Decrement(ref stackCallCount);
        }

        public IContext CloneBackground(CancellationTokenSource token) {
            var childContext = new BackgroundKSPContext(consoleBuffer, token);

            childContexts.Add(childContext);

            return childContext;
        }

        public GameInstance Game => gameInstance;

        public GameMode GameMode => GameModeAdapter.GameModeFromState(Game.GlobalGameState.GetState());

        public double UniversalTime => Game.SpaceSimulation.UniverseModel.UniversalTime;

        public VesselComponent ActiveVessel => gameInstance.ViewController.GetActiveSimVessel(true);

        public KSPConsoleBuffer ConsoleBuffer => consoleBuffer;

        public TimeSeriesCollection TimeSeriesCollection => timeSeriesCollection;

        public KSPOrbitModule.IBody FindBody(string name) {
            var body = Game.ViewController.GetBodyByName(name);

            return body != null ? new BodyWrapper(this, body) : null;
        }

        public object NextYield {
            get {
                object result = nextYield;
                nextYield = new WaitForFixedUpdate();
                return result;
            }
            set => nextYield = value;
        }

        public Action OnNextYieldOnce {
            get => onNextYieldOnce;
            set => onNextYieldOnce = value;
        }

        public void AddMarker(IMarker marker) => markers.Add(marker);

        public void RemoveMarker(IMarker marker) {
            marker.Visible = false;
            markers.Remove(marker);
        }

        public void ClearMarkers() {
            foreach (IMarker marker in markers) marker.Visible = false;
            markers.Clear();
        }

        public void AddResourceTransfer(KSPResourceModule.ResourceTransfer resourceTransfer) {
            resourceTransfers.Add(resourceTransfer);
        }

        public void TriggerMarkerUpdate() {
            try {
                ContextHolder.CurrentContext.Value = this;
                foreach (IMarker marker in markers)
                    marker.OnUpdate();
            } finally {
                ContextHolder.CurrentContext.Value = null;
            }
        }

        public void TriggerMarkerRender() {
            try {
                ContextHolder.CurrentContext.Value = this;
                foreach (IMarker marker in markers)
                    marker.OnRender();
            } finally {
                ContextHolder.CurrentContext.Value = null;
            }
        }

        public bool TryFindAutopilot<T>(VesselComponent vessel, out T autopilot) where T : IKSPAutopilot {
            if (autopilotHooks.ContainsKey(vessel)) {
                return autopilotHooks[vessel].TryFindAutopilot(out autopilot);
            }

            autopilot = default;
            return false;
        }

        public void HookAutopilot(VesselComponent vessel, IKSPAutopilot autopilot) {
            LoggerAdapter.Instance.Debug($"Hook autopilot {autopilot} to {vessel.Name}");
            if (autopilotHooks.ContainsKey(vessel)) {
                autopilotHooks[vessel].Add(autopilot);
            } else {
                AutopilotHooks autopilots = new AutopilotHooks(this);
                autopilots.Add(autopilot);
                autopilotHooks.Add(vessel, autopilots);

                LoggerAdapter.Instance.Debug($"Hooking up for vessel: {vessel.Name}");
                // Ensure that duplicates do no trigger an exception
                vessel.SimulationObject.objVesselBehavior.OnPreAutopilotUpdate -= autopilots.RunAutopilots;
                vessel.SimulationObject.objVesselBehavior.OnPreAutopilotUpdate += autopilots.RunAutopilots;
            }
        }

        public void UnhookAutopilot(VesselComponent vessel, IKSPAutopilot autopilot) {
            if (!autopilotHooks.ContainsKey(vessel)) return;

            LoggerAdapter.Instance.Debug($"Unhook autopilot {autopilot} to {vessel.Name}");

            AutopilotHooks autopilots = autopilotHooks[vessel];

            autopilots.Remove(autopilot);
            if (autopilots.IsEmpty) {
                LoggerAdapter.Instance.Debug($"Unhooking from vessel: {vessel.Name}");
                autopilotHooks.Remove(vessel);
                vessel.SimulationObject.objVesselBehavior.OnPreAutopilotUpdate -= autopilots.RunAutopilots;
            }
        }

        public void UnhookAllAutopilots(VesselComponent vessel) {
            if (!autopilotHooks.ContainsKey(vessel)) return;

            AutopilotHooks autopilots = autopilotHooks[vessel];

            autopilotHooks.Remove(vessel);
            LoggerAdapter.Instance.Debug($"Unhooking from vessel: {vessel.Name}");
            vessel.SimulationObject.objVesselBehavior.OnPreAutopilotUpdate -= autopilots.RunAutopilots;
        }

        public void Cleanup() {
            ClearMarkers();
            foreach (var kv in autopilotHooks) {
                LoggerAdapter.Instance.Debug($"Unhooking from vessel: {kv.Key.Name}");
                if(kv.Key.SimulationObject != null && kv.Key.SimulationObject.objVesselBehavior != null)
                    kv.Key.SimulationObject.objVesselBehavior.OnPreAutopilotUpdate -= kv.Value.RunAutopilots;
            }

            foreach (var resourceTransfer in resourceTransfers) {
                resourceTransfer.Clear();
            }

            foreach (var childContext in childContexts) {
                childContext.Cleanup();
            }

            resourceTransfers.Clear();
            autopilotHooks.Clear();
            childContexts.Clear();
        }
    }

    public class BackgroundKSPContext : IContext {
        private readonly KSPConsoleBuffer consoleBuffer;
        private readonly CancellationTokenSource token;
        private readonly List<BackgroundKSPContext> childContexts;
        private int stackCallCount = 0;

        public BackgroundKSPContext(KSPConsoleBuffer consoleBuffer, CancellationTokenSource token) {
            this.consoleBuffer = consoleBuffer;
            this.token = token;
            childContexts = new List<BackgroundKSPContext>();
        }

        public ITO2Logger Logger => LoggerAdapter.Instance;

        public bool IsBackground => true;

        public void CheckTimeout() => token.Token.ThrowIfCancellationRequested();

        public void ResetTimeout() {
        }

        public IContext CloneBackground(CancellationTokenSource token) {
            var childContext = new BackgroundKSPContext(consoleBuffer, token);

            childContexts.Add(childContext);

            return childContext;
        }

        public void Cleanup() {
            if (token.Token.CanBeCanceled) {
                token.Cancel();
            }

            foreach (var childContext in childContexts) {
                childContext.Cleanup();
            }
        }
        public void FunctionEnter(string name, object[] arguments) {
            if (Interlocked.Increment(ref stackCallCount) > KSPContext.MAX_CALL_STACK) {
                throw new StackOverflowException($"Exceed stack count: {KSPContext.MAX_CALL_STACK}");
            }
        }

        public void FunctionLeave() {
            Interlocked.Decrement(ref stackCallCount);
        }
    }
}
