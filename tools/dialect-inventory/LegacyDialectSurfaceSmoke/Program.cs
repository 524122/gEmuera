using MinorShift.Emuera.Compatibility;

static class Program
{
    private static readonly string[] SnakeOnlyInstructions =
    {
        "CALLSTR", "CLEARIMAGELAYER", "CLEARIMAGELAYER_ALL",
        "HTML_PRINTC", "HTML_PRINTLC", "JUMPSTR", "SET_SKIA_QUALITY", "SET_TEXT_DRAWING_MODE",
        "SETANIMETIMER", "SETIMAGELAYER", "SETIMAGELAYERL", "STRICT_FONT_FALLBACK",
        "TEXT_BGC_OFF", "TEXT_BGC_ON", "TINPUTNF", "TINPUTSNF", "TONEINPUTNF",
        "TONEINPUTSNF", "TRYCALLSTR", "TRYCCALLSTR", "TRYCJUMPSTR", "TRYJUMPSTR",
    };

    private static readonly string[] V24ExcludedFunctions =
    {
        "ACOS", "ARGLEN", "ASIN", "ATAN", "BGMCONTROL", "BITGET", "BITINDEXOFFIRST",
        "BITSET", "BITTOGGLE", "CBGSETCIMG", "CEIL", "COS", "DISABLE_INPUT_MACRO",
        "DT_CELL_GETF", "DT_CELL_SETF", "ENABLE_INPUT_MACRO", "EVAL", "EVALF", "EVALS",
        "EXISTSIMAGELAYER", "FLOOR", "GCLEARLOWALPHA", "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT",
        "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GET_SKIA_QUALITY", "GET_TEXT_DRAWING_MODE",
        "GETANIMETIMER", "GETARGCOUNT", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
        "GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETLINEY", "GETMETHF", "GETPLATFORM",
        "GETSOUNDORBGMINFO", "GETVARF", "GFILLPOLYGON", "G_POLYGON_DRAW", "G_POLYGON_FILL", "GROTATE",
        "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "ISPLAYINGBGM", "ISPLAYINGSOUND",
        "MAP_FINDKEY", "MAP_FROMSTRING", "MAP_MERGE", "MAP_REMOVEIF", "MAP_TOSTRING", "MAP_VALUES",
        "MATCHALL", "MATCHALLEX", "MOUSEBUTTON", "ROUND", "SEQUENCEINPUT", "SIN", "SOUNDCONTROL",
        "SPRITECREATEFROMFILE", "SQL_CONNECT", "SQL_CONNECTION_OPEN", "SQL_DISCONNECT", "SQL_ESCAPE",
        "SQL_EXECUTE_NONQUERY", "SQL_EXECUTE_READER", "SQL_EXECUTE_SCALAR_FLOAT", "SQL_EXECUTE_SCALAR_LONG",
        "SQL_EXECUTE_SCALAR_STRING", "SQL_EXPORT_DT_XML", "SQL_EXPORT_MAP_XML", "SQL_IMPORT_DT_XML",
        "SQL_IMPORT_MAP_XML", "SQL_IMPORT_XML_CUSTOM", "SQL_P_EXECUTE_NONQUERY", "SQL_P_EXECUTE_READER",
        "SQL_P_EXECUTE_SCALAR_FLOAT", "SQL_P_EXECUTE_SCALAR_LONG", "SQL_P_EXECUTE_SCALAR_STRING",
        "SQL_READER_CLOSE", "SQL_READER_GET_FLOAT", "SQL_READER_GET_LONG", "SQL_READER_GET_STRING",
        "SQL_READER_ISNULL", "SQL_READER_READ", "STRFORMCHECK", "TAN", "TOFLOAT", "TOSTRF",
        "UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB", "陥落状態", "陷落状态",
    };

    private static readonly string[] SnakeExcludedFunctions =
    {
        "BITMAP_CACHE_ENABLE", "CBGSETCIMG", "DT_CELL_SETF", "GCLEARLOWALPHA",
        "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT", "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GROTATE",
        "GETARGCOUNT", "GFILLPOLYGON", "MOUSEBUTTON", "SETANIMETIMER", "陥落状態", "陷落状态",
    };

    private static int Main()
    {
        try
        {
            LegacyCompatibilityProfile v24 = LegacyCompatibilityProfile.CreateForProfile("v24pure", true);
            LegacyCompatibilityProfile v24WithoutScopedVariables = LegacyCompatibilityProfile.CreateForProfile("v24pure", false);
            LegacyCompatibilityProfile snake = LegacyCompatibilityProfile.CreateForProfile("snake", true);

            Assert(v24.IsInstructionVisible("PRINT"), "v24 lost a baseline instruction.");
            Assert(v24.IsInstructionVisible("CALLSHARP"), "v24 lost a baseline v24 instruction.");
            Assert(v24.IsInstructionVisible("BITMAP_CACHE_ENABLE") && snake.IsInstructionVisible("BITMAP_CACHE_ENABLE"), "BITMAP_CACHE_ENABLE must be available in both upstream dialects.");
            Assert(v24.IsInstructionVisible("VARI") && v24.IsInstructionVisible("VARS"), "v24 lost enabled scoped-variable instructions.");
            Assert(!v24.IsInstructionVisible("OUTPUTLOG") && !snake.IsInstructionVisible("OUTPUTLOG"), "Godot-port-only instruction leaked into an upstream dialect.");
            Assert(!v24WithoutScopedVariables.IsInstructionVisible("VARI") && !v24WithoutScopedVariables.IsInstructionVisible("VARS"), "disabled scoped-variable instructions leaked into v24.");

            foreach (string key in SnakeOnlyInstructions)
            {
                Assert(!v24.IsInstructionVisible(key), $"Snake instruction leaked into v24: {key}.");
                Assert(snake.IsInstructionVisible(key), $"Snake instruction is missing from Snake: {key}.");
            }

            Assert(!v24.IsFunctionVisible("GROTATE"), "Godot-port-only expression function leaked into v24.");
            foreach (string key in V24ExcludedFunctions)
                Assert(!v24.IsFunctionVisible(key), $"Non-v24 expression function leaked into v24: {key}.");

            Assert(snake.IsFunctionVisible("ACOS"), "Snake lost a Snake expression function.");
            foreach (string key in SnakeExcludedFunctions)
                Assert(!snake.IsFunctionVisible(key), $"Snake exposed a key absent from its reference registry: {key}.");

            Console.WriteLine("Legacy dialect surface smoke passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
