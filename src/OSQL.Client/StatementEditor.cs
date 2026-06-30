using System.Text;

namespace OSQL.Client;

/// <summary>
/// A multi-line statement editor for the REPL. The whole statement (across line
/// breaks) is held in one buffer as text with embedded '\n's, so editing is
/// uniform: arrows move the caret anywhere including across line boundaries,
/// Backspace at the start of a line deletes the preceding '\n' and joins it to
/// the line above, and Enter either inserts a new line or submits the statement.
///
/// Whether Enter submits is decided by the injected <c>isComplete</c> predicate
/// (e.g. "ends with ';'"), evaluated only when the caret is at the end of input.
///
/// Limitations: assumes a logical line plus its prompt fits on one terminal row
/// (no soft-wrap handling) and that the block doesn't scroll off-screen.
/// </summary>
internal sealed class StatementEditor(Func<string, bool> isComplete)
{
    private const string ReadyPrompt = "osql=> ";
    private const string ContinuationPrompt = "osql-> ";
    private const int PromptWidth = 7; // visible width of the prompts above

    private readonly Func<string, bool> _isComplete = isComplete;
    private readonly List<string> _history = [];

    /// <summary>
    /// Read and edit one statement. Returns the submitted text, or a Cancel
    /// (Ctrl+C) / Quit (Ctrl+D or end-of-input) signal.
    /// </summary>
    public ConsoleLine ReadStatement()
    {
        if (Console.IsInputRedirected)
        {
            return ReadPiped();
        }

        var buffer = new StringBuilder();
        var pos = 0;            // caret index within the buffer
        var startTop = Console.CursorTop;
        var renderedRows = 0;   // rows drawn last time, so we can erase shrinkage
        var browsing = _history.Count; // == Count means we're on the live draft
        var draft = string.Empty;

        (int Row, int Col) CaretRowCol()
        {
            var text = buffer.ToString();
            var row = 0;
            var col = 0;
            for (var i = 0; i < pos; i++)
            {
                if (text[i] == '\n')
                {
                    row++;
                    col = 0;
                }
                else
                {
                    col++;
                }
            }

            return (row, col);
        }

        int PosFromRowCol(int targetRow, int targetCol)
        {
            var text = buffer.ToString();
            var row = 0;
            var col = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (row == targetRow && col == targetCol)
                {
                    return i;
                }

                if (text[i] == '\n')
                {
                    if (row == targetRow)
                    {
                        return i; // target column is past this line's end; clamp to it
                    }

                    row++;
                    col = 0;
                }
                else
                {
                    col++;
                }
            }

            return text.Length;
        }

        int RowCount()
        {
            var count = 1;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        void ReplaceAll(string text)
        {
            buffer.Clear();
            buffer.Append(text);
            pos = buffer.Length;
        }

        void SetCaret(int col, int row)
        {
            var c = Math.Clamp(col, 0, Math.Max(0, Console.BufferWidth - 1));
            var r = Math.Clamp(row, 0, Math.Max(0, Console.BufferHeight - 1));
            Console.SetCursorPosition(c, r);
        }

        void Redraw()
        {
            var rows = buffer.ToString().Split('\n');
            for (var i = 0; i < rows.Length; i++)
            {
                SetCaret(0, startTop + i);
                var prompt = i == 0 ? ReadyPrompt : ContinuationPrompt;
                Console.Write(prompt);
                Console.Write(rows[i]);

                var used = prompt.Length + rows[i].Length;
                if (used < Console.BufferWidth - 1)
                {
                    Console.Write(new string(' ', Console.BufferWidth - 1 - used)); // clear tail
                }
            }

            for (var i = rows.Length; i < renderedRows; i++)
            {
                SetCaret(0, startTop + i);
                Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1))); // clear old row
            }

            renderedRows = rows.Length;

            var (caretRow, caretCol) = CaretRowCol();
            SetCaret(PromptWidth + caretCol, startTop + caretRow);
        }

        void Finish()
        {
            Redraw();
            SetCaret(0, startTop + Math.Max(renderedRows, 1) - 1);
            Console.WriteLine();
        }

        Redraw();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (key.Key == ConsoleKey.D)
                {
                    Finish();
                    return ConsoleLine.Quit;
                }

                if (key.Key == ConsoleKey.C)
                {
                    Finish();
                    return ConsoleLine.Cancel;
                }
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (pos == buffer.Length && _isComplete(buffer.ToString()))
                    {
                        var done = buffer.ToString();
                        if (done.Trim().Length > 0)
                        {
                            _history.Add(done);
                        }

                        Finish();
                        return ConsoleLine.FromText(done);
                    }

                    buffer.Insert(pos, '\n');
                    pos++;
                    break;

                case ConsoleKey.LeftArrow:
                    if (pos > 0)
                    {
                        pos--;
                    }

                    break;

                case ConsoleKey.RightArrow:
                    if (pos < buffer.Length)
                    {
                        pos++;
                    }

                    break;

                case ConsoleKey.Home:
                {
                    var (row, _) = CaretRowCol();
                    pos = PosFromRowCol(row, 0);
                    break;
                }

                case ConsoleKey.End:
                {
                    var (row, _) = CaretRowCol();
                    pos = PosFromRowCol(row, int.MaxValue);
                    break;
                }

                case ConsoleKey.Backspace:
                    if (pos > 0)
                    {
                        buffer.Remove(pos - 1, 1); // deleting a '\n' here joins two lines
                        pos--;
                    }

                    break;

                case ConsoleKey.Delete:
                    if (pos < buffer.Length)
                    {
                        buffer.Remove(pos, 1);
                    }

                    break;

                case ConsoleKey.UpArrow:
                {
                    var (row, col) = CaretRowCol();
                    if (row > 0)
                    {
                        pos = PosFromRowCol(row - 1, col);
                    }
                    else if (browsing > 0)
                    {
                        if (browsing == _history.Count)
                        {
                            draft = buffer.ToString();
                        }

                        browsing--;
                        ReplaceAll(_history[browsing]);
                    }

                    break;
                }

                case ConsoleKey.DownArrow:
                {
                    var (row, col) = CaretRowCol();
                    if (row < RowCount() - 1)
                    {
                        pos = PosFromRowCol(row + 1, col);
                    }
                    else if (browsing < _history.Count)
                    {
                        browsing++;
                        ReplaceAll(browsing == _history.Count ? draft : _history[browsing]);
                    }

                    break;
                }

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(pos, key.KeyChar);
                        pos++;
                    }

                    break;
            }

            Redraw();
        }
    }

    /// <summary>
    /// Read a statement from redirected input (a pipe): accumulate raw lines until
    /// the predicate says it's complete, or end-of-input signals quit.
    /// </summary>
    private ConsoleLine ReadPiped()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null)
            {
                return buffer.Length == 0 ? ConsoleLine.Quit : ConsoleLine.FromText(buffer.ToString());
            }

            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }

            buffer.Append(line);
            if (_isComplete(buffer.ToString()))
            {
                return ConsoleLine.FromText(buffer.ToString());
            }
        }
    }
}
