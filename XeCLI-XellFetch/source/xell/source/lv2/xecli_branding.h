/*
 * Added by XeCLI contributors on 2026-03-25 for XeCLI status and branding.
 * This file is part of XeLL Reloaded and is distributed under GPL-2.0-only.
 */

#ifndef XECLI_BRANDING_H
#define XECLI_BRANDING_H

unsigned long xecli_branding_get_heartbeat_ticks(void);
unsigned long xecli_branding_get_state_age_ticks(void);
unsigned long xecli_branding_get_state_age_ms(void);
const char *xecli_branding_get_state_name(void);
const char *xecli_branding_get_status_title(void);
int xecli_branding_format_status(char *buffer, unsigned int buffer_size);
void xecli_branding_apply_theme(void);
void xecli_branding_draw_footer(void);
void xecli_branding_tick(void);
void xecli_branding_set_job(const char *job);
void xecli_branding_set_network_status(const char *status);
void xecli_branding_set_debug_status(const char *status);
void xecli_branding_set_debug_detail(const char *status);
void xecli_branding_set_diag_fields(const char *status);
void xecli_branding_on_initializing(void);
void xecli_branding_on_waiting_for_connection(void);
void xecli_branding_on_dump_started(void);
void xecli_branding_on_dump_progress(int percent);
void xecli_branding_on_verification_progress(int pass, int total);
void xecli_branding_on_verification_percent(int percent);
void xecli_branding_on_dump_complete(void);
void xecli_branding_on_dump_failed(void);
void xecli_branding_on_reboot_requested(void);
void xecli_branding_on_job_finished(int verified);
void xecli_branding_on_job_finished_ex(int verified, int allow_auto_reboot, int pass, int total, const char *path);
void xecli_branding_on_job_failed_ex(const char *reason, const char *path, int pass, int total);
int xecli_branding_should_issue_auto_reboot(void);
void xecli_branding_mark_auto_reboot_attempted(void);
int xecli_branding_should_pause_boot_search(void);

#endif
