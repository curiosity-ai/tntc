using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace TNTc;

public class PassThroughJavaScriptEncoder : JavaScriptEncoder
{
    public override int MaxOutputCharactersPerInputCharacter => 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool WillEncode(int unicodeScalar)
    {
        switch (unicodeScalar)
        {
            case '\n':
            case '"':
            case '\\':
                return true;
            default:
                return false;
        }
    }

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        ReadOnlySpan<char> input = new(text, textLength);
        int                idx   = 0;

        while (Rune.DecodeFromUtf16(input.Slice(idx), out Rune result, out int charsConsumed) == OperationStatus.Done)
        {
            if (WillEncode(result.Value))
            {
                // This character needs to be escaped. Break out.
                break;
            }
            idx += charsConsumed;
        }

        if (idx == input.Length)
        {
            // None of the characters in the string needs to be escaped.
            return -1;
        }
        return idx;
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        numberOfCharactersWritten = 0;var input                    = new ReadOnlySpan<char>(buffer, bufferLength);


        switch (unicodeScalar)
        {
            case '\n':
            {
                *buffer = '\\';
                buffer++;
                numberOfCharactersWritten++;
                *buffer = 'n';
                buffer++;
                numberOfCharactersWritten++;
                return true;
            }
            case '"':
            {
                *buffer = '\\';
                buffer++;
                numberOfCharactersWritten++;
                *buffer = '"';
                buffer++;
                numberOfCharactersWritten++;
                return true;
            }
            case '\\':
            {
                *buffer = '\\';
                buffer++;
                numberOfCharactersWritten++;
                *buffer = '\\';
                buffer++;
                numberOfCharactersWritten++;
                return true;
            }
            default:
            {
                foreach (var c in new Rune(unicodeScalar).ToString())
                {
                    *buffer = c;
                    buffer++;
                    numberOfCharactersWritten++;
                }
                return true;
            }
        }
    }
}