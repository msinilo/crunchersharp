using Dia2Lib;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CruncherSharp
{
    public class LoadPDBTask
    {
        public string FileName { get; set; }
        public string Filter { get; set; }
        public bool SecondPDB { get; set; }
        public bool MatchCase { get; set; }
        public bool WholeExpression { get; set; }
        public bool UseRegularExpression { get; set; }
    }

    public class SymbolAnalyzer
    {
        public Dictionary<string, SymbolInfo> Symbols { get; }
        public SortedSet<string> RootNamespaces { get; }
        public SortedSet<string> Namespaces { get; }
        public string LastError { get; private set; }
        public string FileName { get; set; }

        public SymbolAnalyzer()
        {
            Symbols = new Dictionary<string, SymbolInfo>();
            RootNamespaces = new SortedSet<string>();
            Namespaces = new SortedSet<string>();
            LastError = string.Empty;
        }

        public void Reset()
        {
            RootNamespaces.Clear();
            Namespaces.Clear();
            Symbols.Clear();
            LastError = string.Empty;
            FileName = null;
        }

        public bool LoadPdb(object sender, DoWorkEventArgs e)
        {
            try
            {
                var task = e.Argument as LoadPDBTask;
                FileName = task.FileName;
                if (!LoadPdb(sender, task))
                {
                    e.Cancel = true;
                    return false;
                }
                return true;
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                LastError = exception.ToString();
                return false; 
            }
        }

        private bool LoadPdb(object sender, LoadPDBTask task)
        {
            IDiaDataSource source = new DiaSourceClass();
            source.loadDataFromPdb(FileName);
            source.openSession(out IDiaSession session);
            if (!LoadSymbols(session, sender, task))
                return false;
            RunAnalysis();
            return true;
        }

        private bool LoadSymbols(IDiaSession session, object sender, LoadPDBTask task)
        {
            var worker = sender as BackgroundWorker;
            worker?.ReportProgress(0, "Finding symbols");

            IDiaEnumSymbols allSymbols;

            if (task.Filter.Length > 0)
            {
                uint compareFlags = 0;
                if (!task.WholeExpression || task.UseRegularExpression)
                    compareFlags |= 0x8;
                if (!task.MatchCase)
                    compareFlags |= 0x2;
                else
                    compareFlags |= 0x2;

                if (task.UseRegularExpression)
                    session.findChildren(session.globalScope, SymTagEnum.SymTagUDT, @task.Filter, compareFlags, out allSymbols);
                else if (task.WholeExpression)
                    session.findChildren(session.globalScope, SymTagEnum.SymTagUDT, task.Filter, compareFlags, out allSymbols);
                else
                {
                    string filter = '*' + task.Filter + '*';
                    session.findChildren(session.globalScope, SymTagEnum.SymTagUDT, @filter, compareFlags, out allSymbols);
                }
            }
            else
            {
                session.findChildren(session.globalScope, SymTagEnum.SymTagUDT, null, 0, out allSymbols);
            }

            if (allSymbols == null)
                return false;

            worker?.ReportProgress(0, "Counting symbols");

            var allSymbolsCount = worker != null ? allSymbols.count : 0;
            var i = 0;

            worker?.ReportProgress(0, "Adding symbols");

            foreach (IDiaSymbol sym in allSymbols)
            {
                if (worker != null && worker.CancellationPending)
                {
                    return false;
                }

                if ( task.SecondPDB)
                {
                    SymbolInfo info = FindSymbolInfo(sym.name);
                    if (info != null)
                    {
                        info.NewSize = sym.length;
                    }
                }
                else
                {
                    if (sym.length > 0 && !HasSymbolInfo(sym.name))
                    {
                        var symbolInfo = new SymbolInfo(sym.name, sym.GetType().Name, sym.length);
                        symbolInfo.ProcessChildren(sym);
                        Symbols.Add(symbolInfo.Name, symbolInfo);

                        if (symbolInfo.Name.Contains("::") && !symbolInfo.Name.Contains("<"))
                        {
                            RootNamespaces.Add(symbolInfo.Name.Substring(0, symbolInfo.Name.IndexOf("::")));
                            Namespaces.Add(symbolInfo.Name.Substring(0, symbolInfo.Name.LastIndexOf("::")));
                        }
                    }
                }
                var percentProgress = (int)Math.Round((double)(100 * i++) / allSymbolsCount);
                percentProgress = Math.Max(Math.Min(percentProgress, 99), 1);
                worker?.ReportProgress(percentProgress, String.Format("Adding symbol {0} on {1}", i, allSymbolsCount));
            }


            worker?.ReportProgress(100, String.Format("{0} symbols added", allSymbolsCount));

            return true;
        }

        private void RunAnalysis()
        {
            foreach (var symbol in Symbols.Values)
            {
                symbol.ComputeTotalPadding(this);
                symbol.UpdateBaseClass(this);
            }

            foreach (var symbol in Symbols.Values)
            {
                symbol.CheckOverride();
                symbol.CheckMasking();
            }
        }


        public bool HasSymbolInfo(string name)
        {
            return Symbols.ContainsKey(name);
        }

        public SymbolInfo FindSymbolInfo(string name, bool loadMissingSymbol = false)
        {
            if (!HasSymbolInfo(name) && name.Length > 0)
            {
                if (!loadMissingSymbol)
                    return null;
                var task = new LoadPDBTask
                {
                    FileName = FileName,
                    SecondPDB = false,
                    Filter = name,
                    MatchCase = true,
                    WholeExpression = true,
                    UseRegularExpression = false
                };

                if (!LoadPdb(null, task))
                    return null;
            }

            Symbols.TryGetValue(name, out var symbolInfo);
            return symbolInfo;
        }
    }
}
