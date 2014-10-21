using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SSMSExecutor
{
    public class Executor
    {
        public readonly string CMD_QUERY_EXECUTE = "Query.Execute";

        private EnvDTE.TextDocument document;

        private EnvDTE.EditPoint oldAnchor;
        private EnvDTE.EditPoint oldActivePoint;

        public Executor(EnvDTE.TextDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("EnvDTE.TextDocument");
            }

            this.document = document;

            var selection = this.document.Selection;
            this.oldAnchor = selection.AnchorPoint.CreateEditPoint();
            this.oldActivePoint = selection.ActivePoint.CreateEditPoint();
        }

        private CaretPosition GetCaretPosition()
        {
            var anchor = this.document.Selection.ActivePoint;

            return new CaretPosition
            {
                Line = anchor.Line,
                LineCharOffset = anchor.LineCharOffset
            };
        }

        private string GetDocumentContent()
        {
            var content = string.Empty;
            var selection = this.document.Selection;

            if (!selection.IsEmpty)
            {
                content = selection.Text;
            }
            else
            {
                selection.SelectAll();
                content = selection.Text;

                // restore selection
                selection.MoveToAbsoluteOffset(this.oldAnchor.AbsoluteCharOffset);
                selection.SwapAnchor();
                selection.MoveToAbsoluteOffset(this.oldActivePoint.AbsoluteCharOffset, true);
            }

            return content;
        }

        private void MakeSelection(CaretPosition topPoint, CaretPosition bottomPoint)
        {
            var selection = this.document.Selection;

            selection.MoveToLineAndOffset(topPoint.Line, topPoint.LineCharOffset);
            selection.SwapAnchor();
            selection.MoveToLineAndOffset(bottomPoint.Line, bottomPoint.LineCharOffset, true);
        }

        private bool ParseStatements(string script, out StatementList statementList)
        {
            IList<ParseError> errors;
            TSql100Parser parser = new TSql100Parser(true);

            using (System.IO.StringReader reader = new System.IO.StringReader(script))
            {                
                statementList = parser.ParseStatementList(reader, out errors);
            }

            return errors.Count == 0;
        }

        private CaretCurrentStatement FindCurrentStatement(StatementList statementList, CaretPosition caret)
        {
            if (statementList == null) return null;

            foreach (var statement in statementList.Statements)
            {
                var ft = statementList.ScriptTokenStream[statement.FirstTokenIndex];
                var lt = statementList.ScriptTokenStream[statement.LastTokenIndex];

                if (caret.Line >= ft.Line && caret.Line <= lt.Line)
                {
                    var isBeforeFirstToken = caret.Line == ft.Line && caret.LineCharOffset < ft.Column;
                    var isAfterLastToken = caret.Line == lt.Line && caret.LineCharOffset > lt.Column + lt.Text.Length;

                    if (!(isBeforeFirstToken || isAfterLastToken))
                    {
                        var currentStatement = new CaretCurrentStatement();

                        currentStatement.FirstToken = new CaretPosition
                        {
                            Line = ft.Line,
                            LineCharOffset = ft.Column
                        };

                        currentStatement.LastToken = new CaretPosition
                        {
                            Line = lt.Line,
                            LineCharOffset = lt.Column + lt.Text.Length
                        };

                        return currentStatement;
                    }
                }
            }

            return null;
        }

        private void Exec()
        {
            this.document.DTE.ExecuteCommand(CMD_QUERY_EXECUTE);
        }

        private bool CanExecute()
        {
            try
            {
                var cmd = this.document.DTE.Commands.Item(CMD_QUERY_EXECUTE, -1);
                return cmd.IsAvailable;
            }
            catch
            { }

            return false;
        }

        public void ExecuteCurrentStatement()
        {
            if (!CanExecute())
            {
                return;
            }

            if (!document.Selection.IsEmpty)
            {
                Exec();
            }
            else
            {
                var caret = GetCaretPosition();
                var script = GetDocumentContent();

                StatementList statementList = null;
                if (ParseStatements(script, out statementList))
                {
                    var currentStatement = FindCurrentStatement(statementList, caret);

                    if (currentStatement != null)
                    {
                        // select the statement to be executed
                        MakeSelection(currentStatement.FirstToken, currentStatement.LastToken);

                        // execute the statement
                        Exec();

                        // restore selection
                        MakeSelection(
                            new CaretPosition { Line = oldAnchor.Line, LineCharOffset = oldAnchor.LineCharOffset },
                            new CaretPosition { Line = oldActivePoint.Line, LineCharOffset = oldActivePoint.LineCharOffset });
                    }
                }
                else if (script.IndexOf("go") > 0) {
                    string[] b = new string[] { "go" };
                    int i = 0;
                    int e = 1;
                    int l = 0; int le = 0;
                       
                    string[] t = (script.Split(b, 10, System.StringSplitOptions.RemoveEmptyEntries));
                    foreach (var item in t)
                    {
                        i = e;
                        e = (item.Split(new string[] { "\n" }, System.StringSplitOptions.None).Length) + i - 1;
                        
                        if ((i <= caret.Line) && (caret.Line <= e)) {
                            break;
                        }
                        
                    }
                    //live editing DLL, how?
                    if (i > 1)
                    {
                        i = i + 1;
                    }
                    //TODO make e = e - 1 and linecharoffset  = end of line
                    //there must be an easy way to convert from stringpos to the caretposition.                    
                    MakeSelection(
                          new CaretPosition { Line = i, LineCharOffset = 1 },
                          new CaretPosition { Line = e, LineCharOffset = 1 });
                    Exec();

                    //// restore selection --naaah
                    //MakeSelection(
                    //    new CaretPosition { Line = oldAnchor.Line, LineCharOffset = oldAnchor.LineCharOffset },
                    //    new CaretPosition { Line = oldActivePoint.Line, LineCharOffset = oldActivePoint.LineCharOffset });
                }
                else
                {
                    // there are syntax errors
                    // execute anyway to show the errors
                    Exec();
                }
            }
        }
    }
}