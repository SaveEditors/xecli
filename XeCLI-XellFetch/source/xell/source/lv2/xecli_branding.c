/*
 * Added by XeCLI contributors on 2026-03-25 for XeCLI status and branding.
 * This file is part of XeLL Reloaded and is distributed under GPL-2.0-only.
 */

#include <stdio.h>
#include <string.h>
#include <ctype.h>

#include <console/console.h>

#include "xecli_branding.h"

#define XECLI_COLOR_BACKGROUND 0x00000000
#define XECLI_COLOR_TEXT 0xFFFFFF00
#define XECLI_COLOR_LOGO 0xD6282800
#define XECLI_COLOR_NETWORK_TEXT 0x33D6FF00
#define XECLI_COLOR_ACTIVE_STATUS 0xF77F0000
#define XECLI_COLOR_SUCCESS_STATUS 0x38B00000
#define XECLI_COLOR_FAILURE_STATUS 0xC1121F00
#define XECLI_COLOR_BORDER_HORIZONTAL 0x2563EB00
#define XECLI_COLOR_BORDER_VERTICAL 0xFFFFFF00
#define XECLI_FOOTER_BACKGROUND 0xFFFFFF00
#define XECLI_FOOTER_TEXT 0x00000000
#define XECLI_FOOTER_MARGIN 8
#define XECLI_SHOW_DEBUG 0
#define XECLI_SHOW_BORDER 0
#define XECLI_TICK_MS 10UL
#define XECLI_AUTO_REBOOT_DELAY_TICKS 1000UL
#define XECLI_AUTO_REBOOT_RETRY_TICKS 100UL

enum xecli_dump_state
{
	XECLI_DUMP_STATE_IDLE = 0,
	XECLI_DUMP_STATE_BOOTING = 1,
	XECLI_DUMP_STATE_WAITING = 2,
	XECLI_DUMP_STATE_ACTIVE = 3,
	XECLI_DUMP_STATE_COMPLETE = 4,
	XECLI_DUMP_STATE_FAILED = 5,
	XECLI_DUMP_STATE_REBOOTING = 6
};

enum xecli_job_kind
{
	XECLI_JOB_NAND = 0,
	XECLI_JOB_KEYVAULT = 1
};

static volatile int xecli_dump_state = XECLI_DUMP_STATE_IDLE;
static volatile int xecli_job_kind = XECLI_JOB_NAND;
static volatile unsigned long xecli_heartbeat_ticks = 0;
static volatile unsigned long xecli_state_entered_ticks = 0;
static volatile int xecli_auto_reboot_pending = 0;
static volatile unsigned long xecli_auto_reboot_due_ticks = 0;
static volatile unsigned long xecli_auto_reboot_delay_ticks = XECLI_AUTO_REBOOT_DELAY_TICKS;
static char xecli_state_name[32] = "idle";
static char xecli_status_title[128] = "Idle";
static char xecli_status_line1[160] = "";
static char xecli_status_line2[160] = "";
static char xecli_status_line3[160] = "";
static char xecli_status_path[192] = "";
static char xecli_network_status[96] = "Network: waiting for DHCP";
static char xecli_debug_status[64] = "dbg:init";
static char xecli_debug_detail[64] = "dbg2:init";
static char xecli_diag_fields[768] = "";
static int xecli_waiting_anim_phase = -1;
static int xecli_waiting_title_y = -1;
static int xecli_anim_mode = 0;
static int xecli_anim_phase = -1;
static int xecli_anim_title_y = -1;
static int xecli_status_line1_y = -1;
static int xecli_status_line2_y = -1;
static int xecli_status_line3_y = -1;
static int xecli_last_reboot_seconds = -1;
static int xecli_verify_pass = 0;
static int xecli_verify_total = 0;
static int xecli_dump_percent = -1;
static int xecli_verify_percent = -1;
static char xecli_job_name[24] = "nand";
static const char xecli_footer[] = "Github.com/SaveEditors";
static const char xecli_header[] = "XeCLI | XeLL Network Tools";
static const char xecli_subtitle_1[] = "\xFA\xFA Github @SaveEditors \xFA\xFA";
static const char xecli_subtitle_2[] = "\xFA 2026 \xFA";
static const char *xecli_logo_bitmap[] =
{
	"11100000000001110000000000000000001111110000001110000000000111111111",
	"11110000000011110000000000000000011111111000001110000000000111111111",
	"01111000000111100000000000000000111100000000001100000000000000111000",
	"00111100001111000011111110000001110000000000001100000000000000111000",
	"00011110011110000111111111000001110000000000001100000000000000111000",
	"00001111111100001110000011100001100000000000001100000000000000111000",
	"00001111111100001111111111000001100000000000001100000000000000111000",
	"00011110011110001110000000000001110000000000001100000000000000111000",
	"00111100001111001111111111000000111100000000001111111110000000111000",
	"01111000000111100111111110000000011111111000001111111110000111111111",
	"11110000000011110000000000000000001111110000000111111110000111111111",
	"11100000000001110000000000000000000000000000000000000000000000000000"
};

static void xecli_branding_set_cursor_safe(int x, int y)
{
	if (x < 0)
	{
		x = 0;
	}
	if (y < 0)
	{
		y = 0;
	}
	console_set_cursor(x, y);
}

static int xecli_branding_get_inner_left(void)
{
#if XECLI_SHOW_BORDER
	return 1;
#else
	return 0;
#endif
}

static int xecli_branding_get_inner_right(void)
{
	int max_x = console_get_cursor_max_x();
#if XECLI_SHOW_BORDER
	return (max_x > 1) ? (max_x - 1) : max_x;
#else
	return max_x;
#endif
}

static int xecli_branding_get_inner_width(void)
{
	int inner_left = xecli_branding_get_inner_left();
	int inner_right = xecli_branding_get_inner_right();

	if (inner_right < inner_left)
	{
		return 0;
	}

	return (inner_right - inner_left) + 1;
}

static int xecli_branding_get_center_x(const char *text)
{
	int width;
	int inner_left;
	int len;
	int x;

	if (!text)
	{
		return 0;
	}

	width = xecli_branding_get_inner_width();
	inner_left = xecli_branding_get_inner_left();
	len = (int)strlen(text);
	x = inner_left + ((width - len) / 2);
	if (x < inner_left)
	{
		x = inner_left;
	}

	return x;
}

static void xecli_branding_clear_line_at(int y)
{
	int inner_left;
	int inner_right;
	int i;

	inner_left = xecli_branding_get_inner_left();
	inner_right = xecli_branding_get_inner_right();
	if (inner_right < inner_left)
	{
		return;
	}

	xecli_branding_set_cursor_safe(inner_left, y);
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	for (i = inner_left; i <= inner_right; ++i)
	{
		console_putch(' ');
	}
}

static void xecli_branding_write_centered_line_at(int y, const char *text)
{
	xecli_branding_set_cursor_safe(xecli_branding_get_center_x(text), y);
	printf("%s", text ? text : "");
}

static void xecli_branding_print_centered_line(const char *text)
{
	int y;

	if (!text)
	{
		printf("\n");
		return;
	}

	y = console_get_cursor_y();
	xecli_branding_write_centered_line_at(y, text);
	printf("\n");
}

static void xecli_branding_print_centered_line_colored(const char *text, unsigned int background, unsigned int foreground)
{
	console_set_colors(background, foreground);
	xecli_branding_print_centered_line(text);
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
}

static void xecli_branding_copy_text(char *destination, unsigned int destination_size, const char *source)
{
	if (!destination || destination_size == 0)
	{
		return;
	}

	snprintf(destination, destination_size, "%s", (source && *source) ? source : "");
}

static int xecli_branding_is_keyvault_job(void)
{
	return xecli_job_kind == XECLI_JOB_KEYVAULT;
}

void xecli_branding_set_job(const char *job)
{
	if (job && (!strcmp(job, "kv") || !strcmp(job, "keyvault") || !strcmp(job, "key-vault")))
	{
		xecli_job_kind = XECLI_JOB_KEYVAULT;
		snprintf(xecli_job_name, sizeof(xecli_job_name), "%s", "kv");
		return;
	}

	xecli_job_kind = XECLI_JOB_NAND;
	snprintf(xecli_job_name, sizeof(xecli_job_name), "%s", "nand");
}

static const char *xecli_branding_get_waiting_line1(void)
{
	return "XeCLI on the PC will start the requested task automatically.";
}

static const char *xecli_branding_get_transfer_title(void)
{
	return xecli_branding_is_keyvault_job() ? "Keyvault export in progress" : "NAND dump in progress";
}

static const char *xecli_branding_get_transfer_line2(void)
{
	return xecli_branding_is_keyvault_job() ? "Streaming the keyvault to XeCLI over the network." : "Streaming NAND to XeCLI over the network.";
}

static const char *xecli_branding_get_verification_line2(void)
{
	return xecli_branding_is_keyvault_job() ? "XeCLI is verifying the keyvault export on the PC." : "XeCLI is verifying the NAND dump on the PC.";
}

static const char *xecli_branding_get_transfer_finished_title(void)
{
	return xecli_branding_is_keyvault_job() ? "Keyvault export finished" : "NAND dump finished";
}

static const char *xecli_branding_get_success_title(void)
{
	return xecli_branding_is_keyvault_job() ? "Keyvault export successful" : "NAND dump successful";
}

static const char *xecli_branding_get_complete_title(void)
{
	return xecli_branding_is_keyvault_job() ? "Keyvault export complete" : "NAND dump complete";
}

static const char *xecli_branding_get_failed_title(void)
{
	return xecli_branding_is_keyvault_job() ? "Keyvault export failed" : "NAND dump failed";
}

static const char *xecli_branding_get_generic_failure_line(void)
{
	return xecli_branding_is_keyvault_job() ? "XeCLI could not finish the keyvault export." : "XeCLI could not finish the NAND dump.";
}

static const char *xecli_branding_get_transfer_ended_early_line(void)
{
	return xecli_branding_is_keyvault_job() ? "The transfer ended before XeLL marked the keyvault export complete." : "The transfer ended before XeLL marked the dump complete.";
}

static void xecli_branding_expand_title_text(const char *source, char *destination, unsigned int destination_size)
{
	unsigned int write_index = 0;

	if (!destination || destination_size == 0)
	{
		return;
	}

	if (!source)
	{
		destination[0] = 0;
		return;
	}

	while (*source && write_index + 1 < destination_size)
	{
		char ch = *source++;
		if (ch >= 'a' && ch <= 'z')
		{
			ch = (char)toupper((unsigned char)ch);
		}
		destination[write_index++] = ch;
	}

	destination[write_index] = 0;
}

static void xecli_branding_format_path_line(char *buffer, unsigned int buffer_size, const char *path)
{
	const char *label;
	int max_path_len;
	size_t path_len;

	if (!buffer || buffer_size == 0)
	{
		return;
	}

	label = xecli_branding_is_keyvault_job() ? "Output path: " : "Dump path: ";

	if (!path || !*path)
	{
		snprintf(buffer, buffer_size, "%ssaved on the PC.", label);
		return;
	}

	max_path_len = (int)buffer_size - (int)strlen(label) - 4;
	if (max_path_len < 16)
	{
		max_path_len = 16;
	}

	path_len = strlen(path);
	if ((int)path_len > max_path_len)
	{
		snprintf(buffer, buffer_size, "%s...%s", label, path + (path_len - (size_t)(max_path_len - 3)));
		return;
	}

	snprintf(buffer, buffer_size, "%s%s", label, path);
}

static int xecli_branding_should_emphasize_title(void)
{
	if (xecli_dump_state == XECLI_DUMP_STATE_ACTIVE || xecli_dump_state == XECLI_DUMP_STATE_REBOOTING || xecli_dump_state == XECLI_DUMP_STATE_FAILED)
	{
		return 1;
	}

	if (xecli_dump_state == XECLI_DUMP_STATE_COMPLETE && (!strcmp(xecli_state_name, "verified") || !strcmp(xecli_state_name, "complete")))
	{
		return 1;
	}

	return 0;
}

static unsigned int xecli_branding_get_title_bar_color(void)
{
	if (xecli_dump_state == XECLI_DUMP_STATE_FAILED)
	{
		return XECLI_COLOR_FAILURE_STATUS;
	}
	if (xecli_dump_state == XECLI_DUMP_STATE_REBOOTING)
	{
		return XECLI_COLOR_SUCCESS_STATUS;
	}
	if (xecli_dump_state == XECLI_DUMP_STATE_COMPLETE && !strcmp(xecli_state_name, "verified"))
	{
		return XECLI_COLOR_SUCCESS_STATUS;
	}
	return XECLI_COLOR_ACTIVE_STATUS;
}

static unsigned int xecli_branding_get_title_text_color(void)
{
	if (xecli_dump_state == XECLI_DUMP_STATE_FAILED)
	{
		return XECLI_COLOR_TEXT;
	}
	return XECLI_COLOR_BACKGROUND;
}

static void xecli_branding_write_emphasis_title_at(int y, const char *text)
{
	char expanded[256];

	xecli_branding_expand_title_text(text, expanded, sizeof(expanded));
	console_set_colors(xecli_branding_get_title_bar_color(), xecli_branding_get_title_text_color());
	xecli_branding_write_centered_line_at(y, expanded);
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	xecli_branding_set_cursor_safe(0, y + 1);
}

static void xecli_branding_format_progress_line(char *buffer, unsigned int buffer_size, int percent)
{
	if (!buffer || buffer_size == 0)
	{
		return;
	}

	if (percent < 0)
	{
		snprintf(buffer, buffer_size, "%s", "Progress: starting");
		return;
	}

	if (percent > 100)
	{
		percent = 100;
	}

	snprintf(buffer, buffer_size, "Progress: %d%%", percent);
}

static int xecli_branding_get_reboot_seconds_remaining(void)
{
	unsigned long remaining_ticks;

	if (!xecli_auto_reboot_pending || xecli_auto_reboot_due_ticks <= xecli_heartbeat_ticks)
	{
		return 0;
	}

	remaining_ticks = xecli_auto_reboot_due_ticks - xecli_heartbeat_ticks;
	return (int)((remaining_ticks + 99UL) / 100UL);
}

static void xecli_branding_build_reboot_line(char *buffer, unsigned int buffer_size)
{
	int remaining_seconds;

	if (!buffer || buffer_size == 0)
	{
		return;
	}

	remaining_seconds = xecli_branding_get_reboot_seconds_remaining();
	snprintf(buffer, buffer_size, "Rebooting in %d second%s.", remaining_seconds, (remaining_seconds == 1) ? "" : "s");
}

static void xecli_branding_set_state(int state, const char *state_name, const char *title)
{
	xecli_dump_state = state;
	xecli_branding_copy_text(xecli_state_name, sizeof(xecli_state_name), state_name);
	xecli_branding_copy_text(xecli_status_title, sizeof(xecli_status_title), title);
	xecli_state_entered_ticks = xecli_heartbeat_ticks;
	xecli_last_reboot_seconds = -1;
	xecli_waiting_anim_phase = -1;
	xecli_waiting_title_y = -1;
	xecli_anim_mode = 0;
	xecli_anim_phase = -1;
	xecli_anim_title_y = -1;
	xecli_status_line1_y = -1;
	xecli_status_line2_y = -1;
	xecli_status_line3_y = -1;
}

static void xecli_branding_clear_auto_reboot(void)
{
	xecli_auto_reboot_pending = 0;
	xecli_auto_reboot_due_ticks = 0;
	xecli_auto_reboot_delay_ticks = XECLI_AUTO_REBOOT_DELAY_TICKS;
}

static void xecli_branding_schedule_auto_reboot_with_ticks(unsigned long delay_ticks)
{
	if (delay_ticks == 0)
	{
		delay_ticks = XECLI_AUTO_REBOOT_DELAY_TICKS;
	}

	xecli_auto_reboot_pending = 1;
	xecli_auto_reboot_delay_ticks = delay_ticks;
	xecli_auto_reboot_due_ticks = xecli_heartbeat_ticks + delay_ticks;
	xecli_last_reboot_seconds = -1;
}

static void xecli_branding_schedule_auto_reboot(void)
{
	xecli_branding_schedule_auto_reboot_with_ticks(XECLI_AUTO_REBOOT_DELAY_TICKS);
}

static void xecli_branding_print_blank_lines(int count)
{
	while (count-- > 0)
	{
		printf("\n");
	}
}

static void xecli_branding_write_hr(void)
{
	int width;
	int inner_left;
	int hr_len;
	int x;
	char hr[128];

	width = xecli_branding_get_inner_width();
	inner_left = xecli_branding_get_inner_left();
	if (width <= 0)
	{
		return;
	}

	hr_len = width - 8;
	if (hr_len < 24)
	{
		hr_len = width;
	}
	if (hr_len >= (int)sizeof(hr))
	{
		hr_len = (int)sizeof(hr) - 1;
	}

	memset(hr, '-', (size_t)hr_len);
	hr[hr_len] = '\0';
	x = inner_left + ((width - hr_len) / 2);
	if (x < inner_left)
	{
		x = inner_left;
	}

	xecli_branding_set_cursor_safe(x, console_get_cursor_y());
	printf("%s\n", hr);
}

static void xecli_branding_write_footer(void)
{
	const char *footer_text = xecli_footer;
	int footer_x;
	int footer_y;
	int footer_width;
	int inner_left;
	int inner_right;
	int restore_x;
	int restore_y;
	int footer_len = (int)strlen(footer_text) + 2;

	restore_x = console_get_cursor_x();
	restore_y = console_get_cursor_y();
	inner_left = xecli_branding_get_inner_left();
	inner_right = xecli_branding_get_inner_right();
	footer_width = xecli_branding_get_inner_width();
#if XECLI_SHOW_BORDER
	footer_y = console_get_cursor_max_y() - 1;
#else
	footer_y = console_get_cursor_max_y();
#endif

	if (footer_width <= 0)
	{
		return;
	}

	footer_x = inner_right - footer_len - XECLI_FOOTER_MARGIN + 1;
	if (footer_x < inner_left + XECLI_FOOTER_MARGIN)
	{
		footer_x = inner_left + XECLI_FOOTER_MARGIN;
	}

	if ((footer_x + footer_len) > inner_right)
	{
		footer_x = inner_right - footer_len + 1;
	}

	console_set_colors(XECLI_FOOTER_BACKGROUND, XECLI_FOOTER_TEXT);
	xecli_branding_set_cursor_safe(footer_x, footer_y);
	printf(" %s ", footer_text);
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	xecli_branding_set_cursor_safe(restore_x, restore_y);
}

static void xecli_branding_write_status_line_at(int y, const char *text, int line_index)
{
	if (y < 0)
	{
		return;
	}

	xecli_branding_clear_line_at(y);
	if (!text)
	{
		text = "";
	}

	if (text[0] && (!strncmp(text, "Network", 7) || !strncmp(text, "Dump path:", 10) || !strncmp(text, "Output path:", 12)))
	{
		console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_NETWORK_TEXT);
		xecli_branding_write_centered_line_at(y, text);
		console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
		return;
	}

	if (text[0] && !strncmp(text, "Rebooting in", 12))
	{
		console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_SUCCESS_STATUS);
		xecli_branding_write_centered_line_at(y, text);
		console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
		return;
	}

	if (line_index == 1)
	{
		if (text[0] && !strncmp(text, "Verification passed", 19))
		{
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_SUCCESS_STATUS);
			xecli_branding_write_centered_line_at(y, text);
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
			return;
		}
		if (text[0] && !strncmp(text, "Verification skipped", 20))
		{
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_ACTIVE_STATUS);
			xecli_branding_write_centered_line_at(y, text);
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
			return;
		}
		if (text[0] && (!strncmp(text, "Verification failed", 19) || !strncmp(text, "XeCLI could not", 15)))
		{
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_FAILURE_STATUS);
			xecli_branding_write_centered_line_at(y, text);
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
			return;
		}
		if (text[0] && !strncmp(text, "Progress:", 9))
		{
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_ACTIVE_STATUS);
			xecli_branding_write_centered_line_at(y, text);
			console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
			return;
		}
	}

	xecli_branding_write_centered_line_at(y, text);
}

static void xecli_branding_refresh_status_line1(void)
{
	if (xecli_status_line1_y < 0)
	{
		return;
	}

	xecli_branding_write_status_line_at(xecli_status_line1_y, xecli_status_line1, 1);
	xecli_branding_draw_footer();
}

static void xecli_branding_refresh_status_line(int line_index)
{
	if (line_index == 1 && xecli_status_line1_y >= 0)
	{
		xecli_branding_write_status_line_at(xecli_status_line1_y, xecli_status_line1, 1);
		return;
	}
	if (line_index == 2 && xecli_status_line2_y >= 0)
	{
		xecli_branding_write_status_line_at(xecli_status_line2_y, xecli_status_line2, 2);
		return;
	}
	if (line_index == 3 && xecli_status_line3_y >= 0)
	{
		xecli_branding_write_status_line_at(xecli_status_line3_y, xecli_status_line3, 3);
	}
}

static void xecli_branding_write_debug_status(void)
{
#if !XECLI_SHOW_DEBUG
	return;
#else
	int restore_x;
	int restore_y;
	int debug_y;
	int footer_y;

	restore_x = console_get_cursor_x();
	restore_y = console_get_cursor_y();
	debug_y = console_get_cursor_max_y() - 1;
	footer_y = console_get_cursor_max_y();
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	xecli_branding_set_cursor_safe(1, debug_y);
	printf("DBG1:%-54.54s", xecli_debug_status);
	xecli_branding_set_cursor_safe(1, footer_y);
	printf("DBG2:%-40.40s", xecli_debug_detail);
	xecli_branding_set_cursor_safe(restore_x, restore_y);
#endif
}

static void xecli_branding_write_border(void)
{
#if !XECLI_SHOW_BORDER
	return;
#else
	int restore_x;
	int restore_y;
	int max_x;
	int max_y;
	int x;
	int y;

	restore_x = console_get_cursor_x();
	restore_y = console_get_cursor_y();
	max_x = console_get_cursor_max_x();
	max_y = console_get_cursor_max_y();

	console_set_colors(XECLI_COLOR_BORDER_HORIZONTAL, XECLI_COLOR_BORDER_HORIZONTAL);
	xecli_branding_set_cursor_safe(0, 0);
	for (x = 0; x <= max_x; ++x)
	{
		console_putch(' ');
	}

	xecli_branding_set_cursor_safe(0, max_y);
	for (x = 0; x <= max_x; ++x)
	{
		console_putch(' ');
	}

	console_set_colors(XECLI_COLOR_BORDER_VERTICAL, XECLI_COLOR_BORDER_VERTICAL);
	for (y = 1; y < max_y; ++y)
	{
		xecli_branding_set_cursor_safe(0, y);
		console_putch(' ');
		xecli_branding_set_cursor_safe(max_x, y);
		console_putch(' ');
	}

	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	xecli_branding_set_cursor_safe(restore_x, restore_y);
#endif
}

static void xecli_branding_write_wordmark(void)
{
	size_t row;
	size_t col;
	int logo_width;
	int start_x;
	int start_y;

	logo_width = (int)strlen(xecli_logo_bitmap[0]);
	start_x = xecli_branding_get_inner_left() + ((xecli_branding_get_inner_width() - logo_width) / 2);
	if (start_x < xecli_branding_get_inner_left())
	{
		start_x = xecli_branding_get_inner_left();
	}

	start_y = console_get_cursor_y();

	for (row = 0; row < (sizeof(xecli_logo_bitmap) / sizeof(xecli_logo_bitmap[0])); ++row)
	{
		xecli_branding_set_cursor_safe(start_x, start_y + (int)row);
		for (col = 0; col < strlen(xecli_logo_bitmap[row]); ++col)
		{
			if (xecli_logo_bitmap[row][col] == '1')
			{
				console_set_colors(XECLI_COLOR_LOGO, XECLI_COLOR_LOGO);
			}
			else
			{
				console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
			}

			console_putch(' ');
		}
	}

	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
	xecli_branding_set_cursor_safe(0, start_y + (int)(sizeof(xecli_logo_bitmap) / sizeof(xecli_logo_bitmap[0])));
}

static void xecli_branding_show_status(const char *title, const char *line1, const char *line2, const char *line3)
{
	int title_y;

	xecli_branding_copy_text(xecli_status_title, sizeof(xecli_status_title), title);
	xecli_branding_copy_text(xecli_status_line1, sizeof(xecli_status_line1), line1);
	xecli_branding_copy_text(xecli_status_line2, sizeof(xecli_status_line2), line2);
	xecli_branding_copy_text(xecli_status_line3, sizeof(xecli_status_line3), line3);
	xecli_branding_apply_theme();
	xecli_branding_set_debug_status("show:clear");
	console_clrscr();
	xecli_branding_write_border();
	xecli_branding_set_cursor_safe(xecli_branding_get_inner_left(), 1);
	xecli_branding_print_centered_line(xecli_header);
	xecli_branding_print_blank_lines(1);
	xecli_branding_set_debug_status("show:header");
	xecli_branding_write_hr();
	xecli_branding_print_blank_lines(1);
	xecli_branding_write_wordmark();
	xecli_branding_set_debug_status("show:logo");
	xecli_branding_print_blank_lines(1);
	xecli_branding_write_hr();
	xecli_branding_print_blank_lines(1);
	xecli_branding_print_centered_line(xecli_subtitle_1);
	xecli_branding_print_centered_line(xecli_subtitle_2);
	xecli_branding_print_blank_lines(3);
	xecli_branding_set_debug_status("show:title");
	title_y = console_get_cursor_y();
	if (xecli_branding_should_emphasize_title())
	{
		xecli_branding_write_emphasis_title_at(title_y, xecli_status_title);
	}
	else
	{
		xecli_branding_print_centered_line(xecli_status_title);
	}
	xecli_branding_print_blank_lines(xecli_branding_should_emphasize_title() ? 1 : 2);
	xecli_status_line1_y = console_get_cursor_y();
	xecli_branding_write_status_line_at(xecli_status_line1_y, xecli_status_line1, 1);
	printf("\n");
	xecli_branding_print_blank_lines(1);
	xecli_status_line2_y = console_get_cursor_y();
	xecli_branding_write_status_line_at(xecli_status_line2_y, xecli_status_line2, 2);
	printf("\n");
	xecli_branding_print_blank_lines(1);
	xecli_status_line3_y = console_get_cursor_y();
	xecli_branding_write_status_line_at(xecli_status_line3_y, xecli_status_line3, 3);
	printf("\n");
	xecli_branding_set_debug_status("show:footer");
	xecli_branding_write_footer();
	xecli_branding_set_debug_status("show:done");
	if (xecli_dump_state == XECLI_DUMP_STATE_WAITING)
	{
		xecli_waiting_title_y = title_y;
	}
	if (xecli_anim_mode == 2)
	{
		xecli_anim_title_y = title_y;
	}
}

unsigned long xecli_branding_get_heartbeat_ticks(void)
{
	return xecli_heartbeat_ticks;
}

unsigned long xecli_branding_get_state_age_ticks(void)
{
	if (xecli_heartbeat_ticks < xecli_state_entered_ticks)
	{
		return 0;
	}

	return xecli_heartbeat_ticks - xecli_state_entered_ticks;
}

unsigned long xecli_branding_get_state_age_ms(void)
{
	return xecli_branding_get_state_age_ticks() * XECLI_TICK_MS;
}

const char *xecli_branding_get_state_name(void)
{
	return xecli_state_name;
}

const char *xecli_branding_get_status_title(void)
{
	return xecli_status_title;
}

int xecli_branding_format_status(char *buffer, unsigned int buffer_size)
{
	if (!buffer || buffer_size == 0)
	{
		return 0;
	}

	return snprintf(
		buffer,
		buffer_size,
		"payload=xecli\njob=%s\nstate=%s\ntitle=%s\nheartbeat_ticks=%lu\nheartbeat_ms=%lu\nstate_age_ticks=%lu\nstate_age_ms=%lu\nauto_reboot_pending=%d\nauto_reboot_due_ms=%lu\nfooter=%s\nnetwork_status=%s\nstatus_line1=%s\nstatus_line2=%s\nstatus_line3=%s\nstatus_path=%s\ndump_percent=%d\nverify_percent=%d\nverify_pass=%d\nverify_total=%d\ndebug_status=%s\ndebug_detail=%s\n%s",
		xecli_job_name,
		xecli_branding_get_state_name(),
		xecli_branding_get_status_title(),
		xecli_branding_get_heartbeat_ticks(),
		xecli_branding_get_heartbeat_ticks() * XECLI_TICK_MS,
		xecli_branding_get_state_age_ticks(),
		xecli_branding_get_state_age_ms(),
		xecli_auto_reboot_pending,
		xecli_auto_reboot_pending ? (xecli_auto_reboot_due_ticks * XECLI_TICK_MS) : 0UL,
		xecli_footer,
		xecli_network_status,
		xecli_status_line1,
		xecli_status_line2,
		xecli_status_line3,
		xecli_status_path,
		xecli_dump_percent,
		xecli_verify_percent,
		xecli_verify_pass,
		xecli_verify_total,
		xecli_debug_status,
		xecli_debug_detail,
		xecli_diag_fields);
}

void xecli_branding_apply_theme(void)
{
	console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
}

void xecli_branding_draw_footer(void)
{
	xecli_branding_apply_theme();
	xecli_branding_write_border();
	xecli_branding_write_footer();
	xecli_branding_write_debug_status();
}

void xecli_branding_tick(void)
{
	xecli_heartbeat_ticks++;
	if (xecli_dump_state == XECLI_DUMP_STATE_WAITING)
	{
		int phase = (int)((xecli_heartbeat_ticks / 50UL) % 4UL);
		if (phase != xecli_waiting_anim_phase)
		{
			static const char *suffixes[] = { "", ".", "..", "..." };
			char title[96];

			xecli_waiting_anim_phase = phase;
			if (xecli_waiting_title_y >= 0)
			{
				snprintf(title, sizeof(title), "Waiting for XeCLI connection%s", suffixes[phase]);
				xecli_branding_clear_line_at(xecli_waiting_title_y);
				console_set_colors(XECLI_COLOR_BACKGROUND, XECLI_COLOR_TEXT);
				xecli_branding_write_centered_line_at(xecli_waiting_title_y, title);
				xecli_branding_draw_footer();
			}
		}
	}
	else if (xecli_auto_reboot_pending && xecli_dump_state == XECLI_DUMP_STATE_REBOOTING)
	{
		int remaining_seconds = xecli_branding_get_reboot_seconds_remaining();
		if (remaining_seconds != xecli_last_reboot_seconds)
		{
			xecli_last_reboot_seconds = remaining_seconds;
			if (!strncmp(xecli_status_line2, "Rebooting in", 12))
			{
				xecli_branding_build_reboot_line(xecli_status_line2, sizeof(xecli_status_line2));
				xecli_branding_refresh_status_line(2);
			}
			else
			{
				xecli_branding_build_reboot_line(xecli_status_line3, sizeof(xecli_status_line3));
				xecli_branding_refresh_status_line(3);
			}
		}
	}
	if (xecli_anim_mode == 2)
	{
		int phase = (int)((xecli_heartbeat_ticks / 50UL) % 4UL);
		if (phase != xecli_anim_phase)
		{
			static const char *suffixes[] = { "", ".", "..", "..." };
			char title[96];

			xecli_anim_phase = phase;
			if (xecli_anim_title_y >= 0)
			{
				snprintf(title, sizeof(title), "Verifying%s", suffixes[phase]);
				xecli_branding_copy_text(xecli_status_title, sizeof(xecli_status_title), title);
				xecli_branding_clear_line_at(xecli_anim_title_y);
				xecli_branding_write_emphasis_title_at(xecli_anim_title_y, title);
				xecli_branding_draw_footer();
			}
		}
	}
}

void xecli_branding_set_network_status(const char *status)
{
	if (!status || !*status)
	{
		snprintf(xecli_network_status, sizeof(xecli_network_status), "%s", "Network: unavailable");
		return;
	}

	snprintf(xecli_network_status, sizeof(xecli_network_status), "%s", status);
}

void xecli_branding_set_debug_status(const char *status)
{
	if (!status || !*status)
	{
		snprintf(xecli_debug_status, sizeof(xecli_debug_status), "%s", "dbg:empty");
	}
	else if (strncmp(xecli_debug_status, status, sizeof(xecli_debug_status) - 1) != 0)
	{
		snprintf(xecli_debug_status, sizeof(xecli_debug_status), "%s", status);
	}
	xecli_branding_write_debug_status();
}

void xecli_branding_set_debug_detail(const char *status)
{
	if (!status || !*status)
	{
		snprintf(xecli_debug_detail, sizeof(xecli_debug_detail), "%s", "dbg2:empty");
	}
	else if (strncmp(xecli_debug_detail, status, sizeof(xecli_debug_detail) - 1) != 0)
	{
		snprintf(xecli_debug_detail, sizeof(xecli_debug_detail), "%s", status);
	}
	xecli_branding_write_debug_status();
}

void xecli_branding_set_diag_fields(const char *status)
{
	if (!status || !*status)
	{
		xecli_diag_fields[0] = 0;
		return;
	}

	snprintf(xecli_diag_fields, sizeof(xecli_diag_fields), "%s", status);
}

void xecli_branding_on_initializing(void)
{
	xecli_branding_clear_auto_reboot();
	xecli_branding_set_job("nand");
	xecli_branding_set_state(XECLI_DUMP_STATE_BOOTING, "booting", "Initializing XeLL payload");
	xecli_branding_set_network_status("Network: initializing");
	xecli_branding_set_debug_status("init:show");
	xecli_branding_show_status(
		"Initializing XeLL payload",
		"Preparing the custom XeLL network tools environment.",
		"Bringing up Ethernet and the HTTP control endpoint.",
		"XeCLI connection will become available shortly.");
}

void xecli_branding_on_waiting_for_connection(void)
{
	static const char *suffixes[] = { "", ".", "..", "..." };
	char title[96];

	xecli_branding_clear_auto_reboot();
	xecli_branding_set_state(XECLI_DUMP_STATE_WAITING, "waiting", "Waiting for XeCLI connection");
	xecli_waiting_anim_phase = (int)((xecli_heartbeat_ticks / 50UL) % 4UL);
	xecli_anim_mode = 1;
	snprintf(title, sizeof(title), "Waiting for XeCLI connection%s", suffixes[xecli_waiting_anim_phase]);
	xecli_branding_set_debug_status("wait:show");
	xecli_branding_show_status(
		title,
		xecli_branding_get_waiting_line1(),
		xecli_network_status,
		"Auto reboot only happens after XeCLI confirms verification passed.");
	xecli_branding_set_debug_status("wait:drawn");
}

void xecli_branding_on_dump_started(void)
{
	xecli_branding_clear_auto_reboot();
	xecli_verify_pass = 0;
	xecli_verify_total = 0;
	xecli_dump_percent = 0;
	xecli_verify_percent = -1;
	xecli_status_path[0] = 0;
	xecli_branding_set_state(XECLI_DUMP_STATE_ACTIVE, "active", xecli_branding_get_transfer_title());
	xecli_branding_show_status(
		xecli_branding_get_transfer_title(),
		"Progress: 0%",
		xecli_branding_get_transfer_line2(),
		"Do not power off or reboot the console.");
}

void xecli_branding_on_dump_progress(int percent)
{
	xecli_dump_percent = percent;
	xecli_branding_format_progress_line(xecli_status_line1, sizeof(xecli_status_line1), percent);
	xecli_branding_refresh_status_line1();
}

void xecli_branding_on_verification_progress(int pass, int total)
{
	static const char *suffixes[] = { "", ".", "..", "..." };
	char title[96];

	xecli_branding_clear_auto_reboot();
	xecli_verify_pass = pass;
	xecli_verify_total = total;
	xecli_verify_percent = 0;
	xecli_anim_mode = 2;
	xecli_anim_phase = (int)((xecli_heartbeat_ticks / 50UL) % 4UL);
	snprintf(title, sizeof(title), "Verifying%s", suffixes[xecli_anim_phase]);
	xecli_branding_set_state(XECLI_DUMP_STATE_ACTIVE, "verifying", title);
	xecli_anim_mode = 2;
	xecli_anim_phase = (int)((xecli_heartbeat_ticks / 50UL) % 4UL);
	xecli_branding_show_status(
		title,
		"Progress: 0%",
		xecli_branding_get_verification_line2(),
		"Reboot waits for XeCLI to confirm the dump is valid.");
}

void xecli_branding_on_verification_percent(int percent)
{
	xecli_verify_percent = percent;
	xecli_branding_format_progress_line(xecli_status_line1, sizeof(xecli_status_line1), percent);
	xecli_branding_refresh_status_line1();
}

void xecli_branding_on_dump_complete(void)
{
	xecli_branding_clear_auto_reboot();
	xecli_branding_set_state(XECLI_DUMP_STATE_COMPLETE, "complete", xecli_branding_get_transfer_finished_title());
	xecli_branding_show_status(
		xecli_branding_get_transfer_finished_title(),
		xecli_branding_is_keyvault_job() ? "The keyvault stream finished cleanly." : "The NAND stream finished cleanly.",
		xecli_branding_is_keyvault_job() ? "XeCLI is packaging and verifying the export on the PC." : "XeCLI is packaging and verifying the dump on the PC.",
		"Reboot only after XeCLI says the job is complete.");
}

void xecli_branding_on_dump_failed(void)
{
	xecli_branding_on_job_failed_ex(xecli_branding_get_transfer_ended_early_line(), 0, xecli_verify_pass, xecli_verify_total);
}

void xecli_branding_on_reboot_requested(void)
{
	char line3[160];

	xecli_branding_set_state(XECLI_DUMP_STATE_REBOOTING, "rebooting", "Rebooting console");
	xecli_branding_schedule_auto_reboot_with_ticks(500UL);
	xecli_branding_build_reboot_line(line3, sizeof(line3));
	xecli_branding_show_status(
		"Rebooting console",
		"XeLL accepted the reboot request.",
		"Returning to the dashboard automatically.",
		line3);
}

void xecli_branding_on_job_finished(int verified)
{
	xecli_branding_on_job_finished_ex(verified, verified, xecli_verify_pass, xecli_verify_total, xecli_status_path);
}

void xecli_branding_on_job_finished_ex(int verified, int allow_auto_reboot, int pass, int total, const char *path)
{
	char line1[160];
	char line2[192];
	char line3[160];

	xecli_verify_pass = pass;
	xecli_verify_total = total;
	xecli_branding_copy_text(xecli_status_path, sizeof(xecli_status_path), path);
	if (verified)
	{
		if (allow_auto_reboot)
		{
			xecli_branding_set_state(XECLI_DUMP_STATE_REBOOTING, "verified", xecli_branding_get_success_title());
			xecli_branding_schedule_auto_reboot();
			xecli_branding_build_reboot_line(line2, sizeof(line2));
			xecli_branding_format_path_line(line3, sizeof(line3), xecli_status_path);
		}
		else
		{
			xecli_branding_clear_auto_reboot();
			xecli_branding_set_state(XECLI_DUMP_STATE_COMPLETE, "verified", xecli_branding_get_success_title());
			snprintf(line2, sizeof(line2), "%s", "Final reboot packet missing. Manual reboot required.");
			xecli_branding_format_path_line(line3, sizeof(line3), xecli_status_path);
		}
		snprintf(line1, sizeof(line1), "%s", "Verification passed.");
	}
	else
	{
		xecli_branding_clear_auto_reboot();
		xecli_branding_set_state(XECLI_DUMP_STATE_COMPLETE, "complete", xecli_branding_get_complete_title());
		snprintf(line1, sizeof(line1), "%s", "Verification skipped.");
		snprintf(line2, sizeof(line2), "%s", "Manual reboot required after review.");
		xecli_branding_format_path_line(line3, sizeof(line3), xecli_status_path);
	}
	xecli_branding_show_status(
		verified ? xecli_branding_get_success_title() : xecli_branding_get_complete_title(),
		line1,
		line2,
		line3);
}

void xecli_branding_on_job_failed_ex(const char *reason, const char *path, int pass, int total)
{
	char line1[160];
	char line2[192];
	char line3[160];

	xecli_verify_pass = pass;
	xecli_verify_total = total;
	xecli_branding_copy_text(xecli_status_path, sizeof(xecli_status_path), path);
	xecli_branding_set_state(XECLI_DUMP_STATE_FAILED, "failed", xecli_branding_get_failed_title());
	if (reason && *reason)
	{
		xecli_branding_copy_text(line1, sizeof(line1), reason);
	}
	else if (total > 0 && pass > 0)
	{
		snprintf(line1, sizeof(line1), "%s", "Verification failed.");
	}
	else
	{
		snprintf(line1, sizeof(line1), "%s", xecli_branding_get_generic_failure_line());
	}
	xecli_branding_format_path_line(line2, sizeof(line2), xecli_status_path);
	snprintf(line3, sizeof(line3), "%s", "Manual reboot required after review.");
	xecli_branding_show_status(
		xecli_branding_get_failed_title(),
		line1,
		line2,
		line3);
}

int xecli_branding_should_issue_auto_reboot(void)
{
	return xecli_auto_reboot_pending && xecli_heartbeat_ticks >= xecli_auto_reboot_due_ticks;
}

void xecli_branding_mark_auto_reboot_attempted(void)
{
	if (xecli_auto_reboot_pending)
	{
		xecli_auto_reboot_due_ticks = xecli_heartbeat_ticks + XECLI_AUTO_REBOOT_RETRY_TICKS;
	}
}

int xecli_branding_should_pause_boot_search(void)
{
	return xecli_dump_state != XECLI_DUMP_STATE_IDLE;
}
