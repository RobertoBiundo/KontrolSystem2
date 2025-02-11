﻿using KontrolSystem.TO2.AST;
using KontrolSystem.TO2.Parser;
using KontrolSystem.TO2.Runtime;
using System.Linq;
using KontrolSystem.Parsing;
using KontrolSystem.KSP.Runtime.KSPConsole;
using KontrolSystem.KSP.Runtime.KSPTelemetry;
using KontrolSystem.SpaceWarpMod.Core;

namespace KontrolSystem.SpaceWarpMod.Utils {

    public static class Expression {

        /// <summary>
        /// Submits an expression to be evaluated in the console window. The expression and its result will not be printed to the console window.
        /// </summary>
        /// <param name="expression">The expression to evaluate.</param>
        /// <returns>An object containing the result or an Exception.</returns>
        public static object Run(string expression) {
            var result = TO2ParserREPL.REPLItems.Parse(expression);

            var kspContext = new KSPContext(Mainframe.Instance.Game, Mainframe.Instance.ConsoleBuffer, Mainframe.Instance.TimeSeriesCollection);
            var registry = Mainframe.Instance.LastRegistry;
            var context = new REPLContext(registry, kspContext);

            var pollCount = 0;

            try {
                ContextHolder.CurrentContext.Value = kspContext;

                foreach (var item in result.Where(i => !(i is IBlockItem))) {
                    var future = item.Eval(context);
                    var futureResult = future.PollValue();

                    while (!futureResult.IsReady) {
                        pollCount++;
                        if (pollCount > 100) throw new System.Exception("No result after 100 tries");
                        futureResult = future.PollValue();
                    }
                }

                var mainBlock = new Block(result.OfType<IBlockItem>().ToList());
                var mainFuture = mainBlock.Eval(context);
                var mainFutureResult = mainFuture.PollValue();

                while (!mainFutureResult.IsReady) {
                    pollCount++;
                    if (pollCount > 100) throw new System.Exception("No result after 100 tries");
                    mainFutureResult = mainFuture.PollValue();
                }

                return mainFutureResult.value.Value;

            } finally {
                ContextHolder.CurrentContext.Value = null;
            }
        }
    }
}
