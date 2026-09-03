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

    /// <summary>
    /// The freestanding runtime: the same contract with no operating system
    /// underneath it. Printing goes to the serial port and the VGA text
    /// buffer, reading comes from the serial port, and memory comes from a
    /// bump allocator that never frees — the same rule the hosted runtime
    /// lives by. Floats are not available here yet: their text form needs
    /// the shortest round-trip machinery, which is a project of its own.
    /// </summary>
    /// <remarks>
    /// The pure parts — arithmetic, concatenation, equality — are repeated
    /// from the hosted runtime rather than shared, so that each runtime
    /// reads top to bottom as one piece. The entry point is
    /// <c>caca_boot</c>, called by the boot stub in <c>boot/boot.s</c> once
    /// there is a stack.
    /// </remarks>
    public const string Freestanding = """
        #include <stdbool.h>
        #include <stddef.h>
        #include <stdint.h>

        typedef struct { int32_t length; const char *data; } caca_str;

        /* ---- the machine ---------------------------------------------------- */

        static inline void caca_outb(uint16_t port, uint8_t value) {
            __asm__ volatile ("outb %0, %1" : : "a"(value), "Nd"(port));
        }

        static inline uint8_t caca_inb(uint16_t port) {
            uint8_t value;
            __asm__ volatile ("inb %1, %0" : "=a"(value) : "Nd"(port));
            return value;
        }

        /* A freestanding compiler may still synthesize calls to these. */
        void *memcpy(void *destination, const void *source, size_t count) {
            char *d = destination; const char *s = source;
            while (count--) *d++ = *s++;
            return destination;
        }

        void *memset(void *destination, int value, size_t count) {
            char *d = destination;
            while (count--) *d++ = (char)value;
            return destination;
        }

        int memcmp(const void *a, const void *b, size_t count) {
            const unsigned char *x = a, *y = b;
            for (; count--; x++, y++) {
                if (*x != *y) return *x - *y;
            }
            return 0;
        }

        /* ---- serial (COM1), which QEMU wires to the terminal ---------------- */

        enum { CACA_COM1 = 0x3f8 };

        static void caca_serial_init(void) {
            caca_outb(CACA_COM1 + 1, 0x00);   /* no interrupts; everything is polled */
            caca_outb(CACA_COM1 + 3, 0x80);   /* open the divisor latch */
            caca_outb(CACA_COM1 + 0, 0x03);   /* 38400 baud */
            caca_outb(CACA_COM1 + 1, 0x00);
            caca_outb(CACA_COM1 + 3, 0x03);   /* 8 bits, no parity, one stop */
            caca_outb(CACA_COM1 + 2, 0xc7);
            caca_outb(CACA_COM1 + 4, 0x0b);
        }

        static void caca_serial_put(char c) {
            while (!(caca_inb(CACA_COM1 + 5) & 0x20)) {}
            caca_outb(CACA_COM1, (uint8_t)c);
        }

        static char caca_serial_get(void) {
            while (!(caca_inb(CACA_COM1 + 5) & 0x01)) {}
            return (char)caca_inb(CACA_COM1);
        }

        /* ---- the VGA text buffer, for a real screen ------------------------- */

        static volatile uint16_t *const caca_vga = (volatile uint16_t *)0xb8000;
        static int caca_vga_row = 0;
        static int caca_vga_column = 0;

        static void caca_vga_clear(void) {
            for (int i = 0; i < 80 * 25; i++) caca_vga[i] = 0x0720;
        }

        static void caca_vga_put(char c) {
            if (c == '\n') {
                caca_vga_column = 0;
                caca_vga_row++;
            } else if (c == '\b') {
                /* Erases the character behind the cursor, for editing a line
                   as it is typed. Backing up past the start of the row is not
                   attempted: the input reader never asks for that. */
                if (caca_vga_column > 0) {
                    caca_vga_column--;
                    caca_vga[caca_vga_row * 80 + caca_vga_column] = 0x0720;
                }
            } else {
                caca_vga[caca_vga_row * 80 + caca_vga_column] = (uint16_t)(0x0700 | (uint8_t)c);
                if (++caca_vga_column == 80) { caca_vga_column = 0; caca_vga_row++; }
            }

            if (caca_vga_row == 25) {
                for (int i = 0; i < 24 * 80; i++) caca_vga[i] = caca_vga[i + 80];
                for (int i = 24 * 80; i < 25 * 80; i++) caca_vga[i] = 0x0720;
                caca_vga_row = 24;
            }
        }

        static void caca_put(char c) {
            caca_serial_put(c);
            caca_vga_put(c);
        }

        static void caca_write(const char *data, int32_t length) {
            for (int32_t i = 0; i < length; i++) caca_put(data[i]);
        }

        static void caca_write_z(const char *text) {
            while (*text) caca_put(*text++);
        }

        /* ---- stopping ------------------------------------------------------- */

        /* Port 0xf4 is QEMU's debug-exit device when it is configured; on real
           hardware the write does nothing and the halt below is the end. */
        static void caca_stop(uint8_t code) {
            caca_outb(0xf4, code);
            for (;;) __asm__ volatile ("cli; hlt");
        }

        static void caca_fail(const char *message) {
            caca_write_z("error: ");
            caca_write_z(message);
            caca_put('\n');
            caca_stop(1);
        }

        static void caca_fail_str(const char *prefix, caca_str text, const char *suffix) {
            caca_write_z("error: ");
            caca_write_z(prefix);
            caca_write(text.data, text.length);
            caca_write_z(suffix);
            caca_put('\n');
            caca_stop(1);
        }

        /* ---- memory: a bump allocator that never frees ---------------------- */

        static char caca_heap[1 << 20];
        static size_t caca_heap_used = 0;

        static char *caca_alloc(size_t size) {
            size = (size + 7) & ~(size_t)7;
            if (size > sizeof caca_heap - caca_heap_used) caca_fail("out of memory");
            char *memory = caca_heap + caca_heap_used;
            caca_heap_used += size;
            return memory;
        }

        /* ---- arithmetic: identical to the hosted runtime -------------------- */

        static int32_t caca_add(int32_t a, int32_t b) { return (int32_t)((uint32_t)a + (uint32_t)b); }
        static int32_t caca_sub(int32_t a, int32_t b) { return (int32_t)((uint32_t)a - (uint32_t)b); }
        static int32_t caca_mul(int32_t a, int32_t b) { return (int32_t)((uint32_t)a * (uint32_t)b); }
        static int32_t caca_neg(int32_t a)            { return (int32_t)(0u - (uint32_t)a); }

        static int32_t caca_div(int32_t a, int32_t b) {
            if (b == 0) caca_fail("attempted to divide by zero");
            if (b == -1) return caca_neg(a);
            return a / b;
        }

        static int32_t caca_rem(int32_t a, int32_t b) {
            if (b == 0) caca_fail("attempted to divide by zero");
            if (b == -1) return 0;
            return a % b;
        }

        /* ---- text: identical to the hosted runtime where it can be ---------- */

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
            char reversed[12];
            int count = 0;
            uint32_t magnitude = value < 0 ? 0u - (uint32_t)value : (uint32_t)value;
            do { reversed[count++] = (char)('0' + magnitude % 10); magnitude /= 10; } while (magnitude);
            if (value < 0) reversed[count++] = '-';

            char *data = caca_alloc((size_t)count);
            for (int i = 0; i < count; i++) data[i] = reversed[count - 1 - i];
            return (caca_str){ count, data };
        }

        static caca_str caca_bool_text(bool value) {
            return value ? (caca_str){ 4, "true" } : (caca_str){ 5, "false" };
        }

        /* The shortest round-trip float text needs machinery this runtime does
           not have yet; a program that prints or reads a float stops with an
           honest message rather than printing something subtly different. */
        static caca_str caca_double_text(double value) {
            (void)value;
            caca_fail("floats are not available on bare metal yet");
            return (caca_str){ 0, "" };
        }

        /* ---- print ---------------------------------------------------------- */

        static void caca_print_str(caca_str text) {
            caca_write(text.data, text.length);
            caca_put('\n');
        }

        static void caca_print_int(int32_t value)   { caca_print_str(caca_int_text(value)); }
        static void caca_print_bool(bool value)     { caca_print_str(caca_bool_text(value)); }
        static void caca_print_double(double value) { caca_print_str(caca_double_text(value)); }

        /* ---- PS/2 keyboard, polled, US QWERTY, Scan Code Set 1 --------------
           Read directly by typing into the QEMU window: keys reach the guest
           through the emulated i8042 controller whether or not a serial port
           is attached at all, which is what makes this independent of how
           the machine's serial line is wired. Only the base 104-key layout
           is decoded — no extended (0xE0-prefixed) keys such as the arrows,
           and no NumLock/CapsLock state — which is everything a program that
           reads numbers or short lines of text needs. */

        enum { CACA_PS2_DATA = 0x60, CACA_PS2_STATUS = 0x64 };

        static bool caca_kbd_shift = false;

        /* Index is the scan code with its release bit (0x80) masked off.
           Zero means the key has no character of its own: a modifier, or one
           this driver does not decode. */
        static const char caca_kbd_map[0x3a] = {
            /* 00 */ 0, 0 /* Esc */,
            /* 02 */ '1','2','3','4','5','6','7','8','9','0','-','=','\b',
            /* 0f */ '\t','q','w','e','r','t','y','u','i','o','p','[',']','\n',
            /* 1d */ 0 /* Ctrl */, 'a','s','d','f','g','h','j','k','l',';','\'','`',
            /* 2a */ 0 /* Shift */, '\\','z','x','c','v','b','n','m',',','.','/',
            /* 36 */ 0 /* Shift */, '*' /* keypad */, 0 /* Alt */, ' ',
        };

        static const char caca_kbd_map_shifted[0x3a] = {
            /* 00 */ 0, 0,
            /* 02 */ '!','@','#','$','%','^','&','*','(',')','_','+','\b',
            /* 0f */ '\t','Q','W','E','R','T','Y','U','I','O','P','{','}','\n',
            /* 1d */ 0, 'A','S','D','F','G','H','J','K','L',':','"','~',
            /* 2a */ 0, '|','Z','X','C','V','B','N','M','<','>','?',
            /* 36 */ 0, '*', 0, ' ',
        };

        /* One scan code consumed per call. -1: the controller had nothing
           waiting. -2: it had a byte, but this decoder has nothing to say
           about it — a key release, a modifier press recorded for the next
           real key, or a key outside the decoded set. 0..255: a character. */
        static int caca_kbd_poll(void) {
            if (!(caca_inb(CACA_PS2_STATUS) & 0x01)) return -1;

            uint8_t code = caca_inb(CACA_PS2_DATA);
            bool release = (code & 0x80) != 0;
            uint8_t key = (uint8_t)(code & 0x7f);

            if (key == 0x2a || key == 0x36) { caca_kbd_shift = !release; return -2; }
            if (release || key >= sizeof caca_kbd_map) return -2;

            char c = caca_kbd_shift ? caca_kbd_map_shifted[key] : caca_kbd_map[key];
            return c ? (unsigned char)c : -2;
        }

        /* One byte consumed per call, from whichever a serial line delivers:
           a real terminal's Enter as \r, or a piped newline as \n. -1 when
           none is waiting. */
        static int caca_serial_poll(void) {
            if (!(caca_inb(CACA_COM1 + 5) & 0x01)) return -1;
            return (unsigned char)caca_inb(CACA_COM1);
        }

        /* Blocks for one byte of input, from the keyboard or the serial port,
           whichever produces one first, and echoes it back out both. Reading
           both sources on every call, rather than picking one for the whole
           program, is what lets the same kernel be typed into directly in a
           graphical window and piped into headlessly, unchanged. */
        static char caca_getc(void) {
            for (;;) {
                int k = caca_kbd_poll();
                if (k >= 0) { caca_put((char)k); return (char)k; }

                int s = caca_serial_poll();
                if (s >= 0) {
                    /* A terminal's Enter often sends \r; echoed as itself it
                       would draw as a stray glyph rather than move the
                       cursor down, so the echo is normalized while the
                       original byte is still what the caller sees. */
                    caca_put(s == '\r' ? '\n' : (char)s);
                    return (char)s;
                }
            }
        }

        /* ---- read: one line, from the keyboard or the serial port ----------- */

        static caca_str caca_read_line(void) {
            size_t capacity = 64, length = 0;
            char *data = caca_alloc(capacity);
            for (;;) {
                char c = caca_getc();

                if (c == '\n' || c == '\r') break;

                if (c == '\b') {
                    if (length > 0) length--;
                    continue;
                }

                if (length == capacity) {
                    char *grown = caca_alloc(capacity * 2);
                    memcpy(grown, data, length);
                    data = grown;
                    capacity *= 2;
                }
                data[length++] = c;
            }
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

            int32_t i = 0;
            if (i < text.length && (text.data[i] == '+' || text.data[i] == '-')) i++;
            bool anyDigit = false;
            int64_t magnitude = 0;
            bool negative = text.length > 0 && text.data[0] == '-';
            for (; i < text.length; i++) {
                if (text.data[i] < '0' || text.data[i] > '9') break;
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
            caca_fail("floats are not available on bare metal yet");
            return 0.0;
        }

        static caca_str caca_read_str(void) { return caca_read_line(); }

        /* ---- entry: called by boot/boot.s once there is a stack ------------- */

        int main(void);

        void caca_boot(void) {
            caca_serial_init();
            caca_vga_clear();
            main();
            caca_stop(0);
        }

        """;
}

