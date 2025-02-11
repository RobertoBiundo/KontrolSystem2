﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KontrolSystem.KSP.Runtime.KSPTelemetry;
using UnityEngine;

namespace KontrolSystem.SpaceWarpMod.UI {
    public class TelemetryWindow : ResizableWindow {
        private GLUIDrawer drawer;
        private TimeSeriesCollection timeSeriesCollection;
        private List<string> selectedTimeSeriesNames = new List<string>();
        private Vector2 timeSeriesScrollPos;
        private bool showSave;
        private string saveFileName;

        public event Action OnCloseClicked;

        private static Color[] colors =
        {
            Color.yellow,
            Color.green,
            Color.red,
            Color.white,
            Color.blue,
        };

        public void Awake() {
            Initialize("TimeSeries", new Rect(200, 50, 600, 400), 400, 300, false);

            drawer = new GLUIDrawer(600, 400);
        }

        public void ConnectTo(TimeSeriesCollection timeSeriesCollection) {
            this.timeSeriesCollection = timeSeriesCollection;
            this.selectedTimeSeriesNames.Clear();
        }

        protected override void DrawWindow(int windowId) {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();

            DrawTimeSeriesGraph();

            DrawTimeSeriesSelect();

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Save")) {
                showSave = !showSave;
                saveFileName = Path.Combine(ConfigAdapter.Instance.LocalLibPath, $"TimeSeries-{(long)Game.SpaceSimulation.UniverseModel.UniversalTime}.json");
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close")) {
                OnCloseClicked();
            }

            GUILayout.EndHorizontal();

            if (showSave) {
                GUILayout.BeginHorizontal();

                saveFileName = GUILayout.TextField(saveFileName, GUILayout.MinWidth(200), GUILayout.MaxWidth(2000), GUILayout.ExpandWidth(true));

                if (GUILayout.Button("Save")) {
                    timeSeriesCollection.SaveJson(saveFileName);
                    showSave = !showSave;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawTimeSeriesSelect() {
            GUILayout.BeginVertical();

            timeSeriesScrollPos = GUILayout.BeginScrollView(timeSeriesScrollPos, GUILayout.MinWidth(200));
            GUILayout.BeginVertical();

            if (timeSeriesCollection != null) {
                foreach (var timeSeries in timeSeriesCollection.AllTimeSeries) {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Toggle(selectedTimeSeriesNames.Contains(timeSeries.Name), timeSeries.Name)) {
                        if (!selectedTimeSeriesNames.Contains(timeSeries.Name)) selectedTimeSeriesNames.Add(timeSeries.Name);
                    } else {
                        selectedTimeSeriesNames.Remove(timeSeries.Name);
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("X")) {
                        timeSeriesCollection.RemoveTimeSeries(timeSeries.Name);
                        selectedTimeSeriesNames.Remove(timeSeries.Name);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        private void DrawTimeSeriesGraph() {
            GUILayout.BeginVertical();
            var graphRect = GUILayoutUtility.GetRect(200, 200,
                new GUILayoutOption[]
                {
                    GUILayout.MinWidth(200), GUILayout.MaxWidth(3000), GUILayout.ExpandWidth(true),
                    GUILayout.MinHeight(200), GUILayout.MaxHeight(3000), GUILayout.ExpandHeight(true)
                });
            var width = (int)graphRect.width;
            var height = (int)graphRect.height;
            if (width > 1 && height > 1) drawer.Resize(width, height);

            DrawGraph();

            GUI.DrawTexture(graphRect, drawer.Texture);

            GUILayout.EndVertical();
        }

        private void DrawGraph() {
            using (var draw = drawer.Draw()) {
                if (selectedTimeSeriesNames.Count == 0) {
                    draw.DrawText(new Vector2(draw.Width / 2, draw.Height / 2), "No data", 50, new Vector2(0.5f, 0.5f), 0, Color.yellow);
                } else {
                    var selectedTimeSeries =
                        timeSeriesCollection.AllTimeSeries.Where(t => selectedTimeSeriesNames.Contains(t.Name));
                    var offsetTop = 2;
                    var offsetBottom = 18;
                    var offsetLeft = 18;
                    var offsetRight = 2;

                    var startUT = selectedTimeSeries.Min(t => t.StartUt);
                    var startUTStr = startUT.ToString("F2", CultureInfo.InvariantCulture);
                    var endUT = selectedTimeSeries.Max(t => t.EndUt);
                    var endUTStr = endUT.ToString("F2", CultureInfo.InvariantCulture); ;

                    draw.DrawText(new Vector2(offsetLeft, 1), startUTStr, 16, new Vector2(0, 0), 0, Color.white);
                    draw.DrawText(new Vector2(draw.Width - offsetRight, 1), endUTStr, 16, new Vector2(1, 0), 0, Color.white);

                    var allValues = selectedTimeSeries.Select(t => t.Values).ToArray();
                    var min = allValues.SelectMany(value => value).Min(v => v.Item2.min);
                    var minStr = min.ToString("F3", CultureInfo.InvariantCulture);
                    var max = allValues.SelectMany(value => value).Max(v => v.Item2.max);
                    var maxStr = max.ToString("F3", CultureInfo.InvariantCulture);

                    draw.DrawText(new Vector2(offsetLeft - 2, offsetBottom), minStr, 16, new Vector2(0, 0), 90, Color.white);
                    draw.DrawText(new Vector2(offsetLeft - 2, draw.Height - offsetTop), maxStr, 16, new Vector2(1, 0), 90, Color.white);

                    var xScale = (endUT - startUT) / (draw.Width - offsetLeft - offsetRight);
                    var yScale = (max - min) / (draw.Height - offsetBottom - offsetTop);

                    if (min < 0 && max > 0) {
                        draw.Polygon(new Vector2[] {
                            new Vector2(offsetLeft,  (float)((0 - min) / yScale) + offsetBottom),
                            new Vector2(draw.Width - offsetRight,  (float)((0 - min) / yScale) + offsetBottom)
                        }, Color.gray);
                    }

                    for (int i = 0; i < allValues.Length; i++) {
                        var color = colors[i % colors.Length];

                        draw.LineTube(allValues[i].Select(i =>
                            new Vector3((float)((i.Item1 - startUT) / xScale) + offsetLeft,
                                (float)((i.Item2.min - min) / yScale) + offsetBottom,
                                (float)((i.Item2.max - min) / yScale) + offsetBottom)).ToArray(), new Color(color.r, color.g, color.b, 0.5f));

                        draw.Polygon(allValues[i].Select(i =>
                            new Vector2((float)((i.Item1 - startUT) / xScale) + offsetLeft,
                                (float)((i.Item2.avg - min) / yScale) + offsetBottom)).ToArray(), color);
                    }

                    draw.Polygon(new Vector2[] {
                        new Vector2(offsetLeft, offsetBottom),
                        new Vector2(offsetLeft, draw.Height - offsetTop ),
                        new Vector2(draw.Width - offsetRight, draw.Height - offsetTop),
                        new Vector2(draw.Width - offsetRight, offsetBottom)
                    }, Color.white, true);
                }
            }
        }
    }
}
