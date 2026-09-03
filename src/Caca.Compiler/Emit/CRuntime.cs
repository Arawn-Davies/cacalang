namespace Caca.Emit;

/// <summary>
/// The C runtime emitted at the top of every generated program.
/// </summary>
/// <remarks>
/// Generated code reaches the outside world only through the <c>caca_</c>
/// functions declared here, so a different environment — a freestanding one,
/// booted without an operating system — only has to supply this file's
/// contract, not libc. This hosted implementation exists first because it can
/// be compiled with any C compiler and compared against the interpreter,
/// which is what keeps a third backend honest.
/// </remarks>
public static class CRuntime
{
    /// <summary>The hosted (libc) runtime, as C source.</summary>
    public const string Hosted = """
        #include <inttypes.h>
        #include <math.h>
        #include <stdbool.h>
        #include <stdint.h>
        #include <stdio.h>
        #include <stdlib.h>
        #include <string.h>

        /* A string: a length and bytes, immutable once made. The length is
           carried rather than found with strlen so that embedded zero bytes
           survive, as they do in the other backends. Allocations are never
           freed: programs in this language are small and short-lived, and the
           freestanding runtime will use a bump allocator with the same rule. */
        typedef struct { int32_t length; const char *data; } caca_str;

        /* A runtime error stops the program the way the interpreter does:
           one readable line, and a failing exit code. */
        static void caca_fail_str(const char *prefix, caca_str text, const char *suffix) {
            fputs("error: ", stderr);
            fputs(prefix, stderr);
            fwrite(text.data, 1, (size_t)text.length, stderr);
            fputs(suffix, stderr);
            fputc('\n', stderr);
            exit(2);
        }

        static void caca_fail(const char *message) {
            fprintf(stderr, "error: %s\n", message);
            exit(2);
        }

        static char *caca_alloc(size_t size) {
            char *memory = (char *)malloc(size ? size : 1);
            if (!memory) caca_fail("out of memory");
            return memory;
        }

        /* ---- arithmetic ----------------------------------------------------
           Ints wrap on overflow, as they do in the other two backends. Signed
           overflow is undefined behaviour in C, so the arithmetic is done on
           the unsigned representation, which is defined to wrap. */

        static int32_t caca_add(int32_t a, int32_t b) { return (int32_t)((uint32_t)a + (uint32_t)b); }
        static int32_t caca_sub(int32_t a, int32_t b) { return (int32_t)((uint32_t)a - (uint32_t)b); }
        static int32_t caca_mul(int32_t a, int32_t b) { return (int32_t)((uint32_t)a * (uint32_t)b); }
        static int32_t caca_neg(int32_t a)            { return (int32_t)(0u - (uint32_t)a); }

        static int32_t caca_div(int32_t a, int32_t b) {
            if (b == 0) caca_fail("attempted to divide by zero");
            /* INT32_MIN / -1 overflows; wrapping it mirrors the wrapping of
               every other int operation rather than trapping in hardware. */
            if (b == -1) return caca_neg(a);
            return a / b;
        }

        static int32_t caca_rem(int32_t a, int32_t b) {
            if (b == 0) caca_fail("attempted to divide by zero");
            if (b == -1) return 0;
            return a % b;
        }

        /* ---- text ---------------------------------------------------------- */

        static caca_str caca_concat(caca_str a, caca_str b) {
            char *data = caca_alloc((size_t)a.length + (size_t)b.length);
            memcpy(data, a.data, (size_t)a.length);
            memcpy(data + a.length, b.data, (size_t)b.length);
            return (caca_str){ a.length + b.length, data };
        }

        static bool caca_str_eq(caca_str a, caca_str b) {
            return a.length == b.length && memcmp(a.data, b.data, (size_t)a.length) == 0;
        }

        static caca_str caca_int_text(int32_t value) {
            char buffer[16];
            int length = snprintf(buffer, sizeof buffer, "%" PRId32, value);
            char *data = caca_alloc((size_t)length);
            memcpy(data, buffer, (size_t)length);
            return (caca_str){ length, data };
        }

        static caca_str caca_bool_text(bool value) {
            return value ? (caca_str){ 4, "true" } : (caca_str){ 5, "false" };
        }

        /* Renders a float exactly as the other backends do: the shortest
           decimal that reads back as the same value, laid out the way .NET
           lays it out — fixed notation while the leading digit sits between
           1e-4 and 1e15, scientific as "d.dddE+XX" beyond that — with a
           trailing ".0" when the result would otherwise read as an int. */
        static caca_str caca_double_text(double value) {
            if (isnan(value))  return (caca_str){ 3, "NaN" };
            if (isinf(value))  return value < 0 ? (caca_str){ 9, "-Infinity" } : (caca_str){ 8, "Infinity" };

            /* The shortest digit string is found by rounding to 1..17
               significant digits and taking the first that round-trips.
               printf rounds to nearest, which is also what the shortest
               round-trip formatters pick. */
            char scientific[40];
            for (int precision = 1; precision <= 17; precision++) {
                snprintf(scientific, sizeof scientific, "%.*e", precision - 1, value);
                if (strtod(scientific, NULL) == value) break;
            }

            /* Pull the pieces out of "-d.dddddde+xxx". */
            char digits[20];
            int digitCount = 0;
            bool negative = scientific[0] == '-';
            int exponent = 0;
            {
                const char *p = scientific + (negative ? 1 : 0);
                for (; *p && *p != 'e'; p++) {
                    if (*p != '.') digits[digitCount++] = *p;
                }
                exponent = (int)strtol(p + 1, NULL, 10);
            }

            /* Trailing zeros carry no information; printf keeps them when the
               requested precision exceeds them mid-loop, the shortest form
               does not. */
            while (digitCount > 1 && digits[digitCount - 1] == '0') digitCount--;

            char out[40];
            int n = 0;
            if (negative) out[n++] = '-';

            if (exponent >= -4 && exponent <= 14) {
                /* Fixed notation. */
                if (exponent >= 0) {
                    for (int i = 0; i <= exponent; i++) out[n++] = i < digitCount ? digits[i] : '0';
                    if (digitCount > exponent + 1) {
                        out[n++] = '.';
                        for (int i = exponent + 1; i < digitCount; i++) out[n++] = digits[i];
                    }
                } else {
                    out[n++] = '0';
                    out[n++] = '.';
                    for (int i = 0; i < -exponent - 1; i++) out[n++] = '0';
                    for (int i = 0; i < digitCount; i++) out[n++] = digits[i];
                }
            } else {
                /* Scientific notation, .NET style: 1.5E-05, 1E+300. */
                out[n++] = digits[0];
                if (digitCount > 1) {
                    out[n++] = '.';
                    for (int i = 1; i < digitCount; i++) out[n++] = digits[i];
                }
                n += snprintf(out + n, sizeof out - (size_t)n, "E%+03d", exponent);
            }

            /* 1 would read as an int; 1.0 does not. */
            bool hasMark = false;
            for (int i = 0; i < n; i++) {
                if (out[i] == '.' || out[i] == 'E') { hasMark = true; break; }
            }
            if (!hasMark) { out[n++] = '.'; out[n++] = '0'; }

            char *data = caca_alloc((size_t)n);
            memcpy(data, out, (size_t)n);
            return (caca_str){ n, data };
        }

        /* ---- print --------------------------------------------------------- */

        static void caca_print_str(caca_str text) {
            fwrite(text.data, 1, (size_t)text.length, stdout);
            fputc('\n', stdout);
        }

        static void caca_print_int(int32_t value)   { printf("%" PRId32 "\n", value); }
        static void caca_print_bool(bool value)     { caca_print_str(caca_bool_text(value)); }
        static void caca_print_double(double value) { caca_print_str(caca_double_text(value)); }

        /* ---- read ----------------------------------------------------------
           One line from standard input; the end of input reads as an empty
           line, exactly as it does in the other backends. */

        static caca_str caca_read_line(void) {
            size_t capacity = 64, length = 0;
            char *data = caca_alloc(capacity);
            int c;
            while ((c = fgetc(stdin)) != EOF && c != '\n') {
                if (length == capacity) {
                    char *grown = caca_alloc(capacity * 2);
                    memcpy(grown, data, length);
                    data = grown;
                    capacity *= 2;
                }
                data[length++] = (char)c;
            }
            /* A line ended by \r\n keeps no \r, matching Console.ReadLine. */
            if (length > 0 && data[length - 1] == '\r') length--;
            return (caca_str){ (int32_t)length, data };
        }

        static caca_str caca_trimmed(caca_str line) {
            int32_t start = 0, end = line.length;
            while (start < end && (unsigned char)line.data[start] <= ' ') start++;
            while (end > start && (unsigned char)line.data[end - 1] <= ' ') end--;
            return (caca_str){ end - start, line.data + start };
        }

        static int32_t caca_read_int(void) {
            caca_str line = caca_read_line();
            caca_str text = caca_trimmed(line);

            /* Only an optional sign and digits: strtol also takes hex and
               other things the language does not. */
            int32_t i = 0;
            if (i < text.length && (text.data[i] == '+' || text.data[i] == '-')) i++;
            bool anyDigit = false;
            int64_t magnitude = 0;
            bool negative = text.length > 0 && text.data[0] == '-';
            for (; i < text.length; i++) {
                if (text.data[i] < '0' || text.data[i] > '9') { anyDigit = false; break; }
                anyDigit = true;
                magnitude = magnitude * 10 + (text.data[i] - '0');
                if (magnitude > (int64_t)INT32_MAX + 1) break;
            }
            int64_t value = negative ? -magnitude : magnitude;
            if (!anyDigit || i != text.length || value < INT32_MIN || value > INT32_MAX) {
                caca_fail_str("'", line, "' is not an integer");
            }
            return (int32_t)value;
        }

        static double caca_read_double(void) {
            caca_str line = caca_read_line();
            caca_str text = caca_trimmed(line);

            /* Sign, digits, a point, an exponent — and nothing else: strtod
               also takes "inf", "nan" and hex floats, which the language
               does not. */
            int32_t i = 0;
            bool anyDigit = false;
            if (i < text.length && (text.data[i] == '+' || text.data[i] == '-')) i++;
            while (i < text.length && text.data[i] >= '0' && text.data[i] <= '9') { i++; anyDigit = true; }
            if (i < text.length && text.data[i] == '.') {
                i++;
                while (i < text.length && text.data[i] >= '0' && text.data[i] <= '9') { i++; anyDigit = true; }
            }
            if (anyDigit && i < text.length && (text.data[i] == 'e' || text.data[i] == 'E')) {
                i++;
                if (i < text.length && (text.data[i] == '+' || text.data[i] == '-')) i++;
                bool exponentDigit = false;
                while (i < text.length && text.data[i] >= '0' && text.data[i] <= '9') { i++; exponentDigit = true; }
                if (!exponentDigit) anyDigit = false;
            }
            if (!anyDigit || i != text.length) {
                caca_fail_str("'", line, "' is not a number");
            }

            char *copy = caca_alloc((size_t)text.length + 1);
            memcpy(copy, text.data, (size_t)text.length);
            copy[text.length] = 0;
            return strtod(copy, NULL);
        }

        static caca_str caca_read_str(void) { return caca_read_line(); }

        """;
}
