/*
 * XeCLI-specific changes added by XeCLI contributors on 2026-03-25.
 * This file remains part of XeLL Reloaded and is distributed under GPL-2.0-only.
 */

#include <fcntl.h>
#include <unistd.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <debug.h>
#include <xenos/xenos.h>
#include <console/console.h>
#include <time/time.h>
#include <ppc/timebase.h>
#include <usb/usbmain.h>
#include <sys/iosupport.h>
#include <ppc/register.h>
#include <xenon_nand/xenon_sfcx.h>
#include <xenon_nand/xenon_config.h>
#include <xenon_soc/xenon_secotp.h>
#include <xenon_soc/xenon_power.h>
#include <xenon_soc/xenon_io.h>
#include <xenon_sound/sound.h>
#include <xenon_smc/xenon_smc.h>
#include <xenon_smc/xenon_gpio.h>
#include <xb360/xb360.h>
#include <network/network.h>
#include <lwip/ip_addr.h>
#include <lwip/stats.h>
#include <netif/etharp.h>
#include <httpd/httpd.h>
#include <elf/elf.h>
#include <version.h>
#include <byteswap.h>

#include "asciiart.h"
#include "config.h"

#include "log.h"
#include "xecli_branding.h"

extern struct netif netif;

static void xecli_update_network_status(void)
{
#ifndef NO_NETWORKING
	char status[96];
	const char *ip_text = "no-ip";

	if (netif.ip_addr.addr != 0)
	{
		ip_text = ipaddr_ntoa(&netif.ip_addr);
	}

	snprintf(status, sizeof(status), "Network IP: %s", ip_text);
	xecli_branding_set_network_status(status);
#else
	xecli_branding_set_network_status("Network: disabled");
#endif
}

static void xecli_update_loop_debug(const char *stage, unsigned long network_poll_count, unsigned long usb_poll_count)
{
	char debug_status[64];
	char debug_detail[64];
	char diag_fields[1536];
	char http_fields[512];
	char ip_text[32];
	char netmask_text[32];
	char gateway_text[32];
	unsigned long http_accept_count = 0;
	unsigned long http_request_count = 0;
	unsigned long http_recv_count = 0;
	unsigned long http_error_count = 0;
	unsigned long flags = 0;
	unsigned int mtu = 0;
	unsigned int link_recv = 0;
	unsigned int link_xmit = 0;
	unsigned int link_drop = 0;
	unsigned int etharp_recv = 0;
	unsigned int etharp_xmit = 0;
	unsigned int etharp_drop = 0;
	unsigned int ip_recv = 0;
	unsigned int ip_xmit = 0;
	unsigned int ip_drop = 0;
	unsigned int tcp_recv = 0;
	unsigned int tcp_xmit = 0;
	unsigned int tcp_drop = 0;
	int http_bind_err = 0;
	int http_listen_ready = 0;
	int ip_assigned = 0;

	httpd_get_xecli_debug_stats(0, 0, &http_bind_err, &http_listen_ready, &http_accept_count, &http_request_count, &http_recv_count, 0, 0, 0, &http_error_count);
	httpd_format_xecli_debug_stats(http_fields, sizeof(http_fields));
#ifndef NO_NETWORKING
	flags = netif.flags;
	ip_assigned = (netif.ip_addr.addr != 0);
	mtu = (unsigned int)netif.mtu;
	snprintf(ip_text, sizeof(ip_text), "%s", ip_assigned ? ipaddr_ntoa(&netif.ip_addr) : "0.0.0.0");
	snprintf(netmask_text, sizeof(netmask_text), "%s", ipaddr_ntoa(&netif.netmask));
	snprintf(gateway_text, sizeof(gateway_text), "%s", ipaddr_ntoa(&netif.gw));
	link_recv = (unsigned int)lwip_stats.link.recv;
	link_xmit = (unsigned int)lwip_stats.link.xmit;
	link_drop = (unsigned int)lwip_stats.link.drop;
	etharp_recv = (unsigned int)lwip_stats.etharp.recv;
	etharp_xmit = (unsigned int)lwip_stats.etharp.xmit;
	etharp_drop = (unsigned int)lwip_stats.etharp.drop;
	ip_recv = (unsigned int)lwip_stats.ip.recv;
	ip_xmit = (unsigned int)lwip_stats.ip.xmit;
	ip_drop = (unsigned int)lwip_stats.ip.drop;
	tcp_recv = (unsigned int)lwip_stats.tcp.recv;
	tcp_xmit = (unsigned int)lwip_stats.tcp.xmit;
	tcp_drop = (unsigned int)lwip_stats.tcp.drop;
#else
	snprintf(ip_text, sizeof(ip_text), "%s", "disabled");
	snprintf(netmask_text, sizeof(netmask_text), "%s", "disabled");
	snprintf(gateway_text, sizeof(gateway_text), "%s", "disabled");
#endif

	snprintf(debug_status, sizeof(debug_status), "st=%s h%lu n%lu f%lX ip%d lr%u ar%u ir%u tr%u", stage, xecli_branding_get_heartbeat_ticks(), network_poll_count, flags, ip_assigned, link_recv, etharp_recv, ip_recv, tcp_recv);
	snprintf(debug_detail, sizeof(debug_detail), "a%lu q%lu r%lu l%d b%d e%lu tx%u ix%u ax%u", http_accept_count, http_request_count, http_recv_count, http_listen_ready, http_bind_err, http_error_count, tcp_xmit, ip_xmit, etharp_xmit);
	snprintf(
		diag_fields,
		sizeof(diag_fields),
		"debug_stage=%s\nloop_network_poll_count=%lu\nloop_usb_poll_count=%lu\nnetif_flags_hex=%08lX\nnetif_ip_assigned=%d\nnetif_ip=%s\nnetif_netmask=%s\nnetif_gateway=%s\nnetif_mtu=%u\nlwip_link_recv=%u\nlwip_link_xmit=%u\nlwip_link_drop=%u\nlwip_etharp_recv=%u\nlwip_etharp_xmit=%u\nlwip_etharp_drop=%u\nlwip_ip_recv=%u\nlwip_ip_xmit=%u\nlwip_ip_drop=%u\nlwip_tcp_recv=%u\nlwip_tcp_xmit=%u\nlwip_tcp_drop=%u\n%s",
		stage,
		network_poll_count,
		usb_poll_count,
		flags,
		ip_assigned,
		ip_text,
		netmask_text,
		gateway_text,
		mtu,
		link_recv,
		link_xmit,
		link_drop,
		etharp_recv,
		etharp_xmit,
		etharp_drop,
		ip_recv,
		ip_xmit,
		ip_drop,
		tcp_recv,
		tcp_xmit,
		tcp_drop,
		http_fields);
	xecli_branding_set_debug_status(debug_status);
	xecli_branding_set_debug_detail(debug_detail);
	xecli_branding_set_diag_fields(diag_fields);
}

void do_asciiart()
{
	char *p = asciiart;
	while (*p)
		console_putch(*p++);
	printf(asciitail);
}

void dumpana() {
	int i;
	for (i = 0; i < 0x100; ++i)
	{
		uint32_t v;
		xenon_smc_ana_read(i, &v);
		printf("0x%08x, ", (unsigned int)v);
		if ((i&0x7)==0x7)
			printf(" // %02x\n", (unsigned int)(i &~0x7));
	}
}

char FUSES[350]; /* this string stores the ascii dump of the fuses */

unsigned char stacks[6][0x10000];

void reset_timebase_task()
{
	mtspr(284,0); // TBLW
	mtspr(285,0); // TBUW
	mtspr(284,0);
}

void synchronize_timebases()
{
	xenon_thread_startup();
	
	std((void*)0x200611a0,0); // stop timebase
	
	int i;
	for(i=1;i<6;++i){
		xenon_run_thread_task(i,&stacks[i][0xff00],(void *)reset_timebase_task);
		while(xenon_is_thread_task_running(i));
	}
	
	reset_timebase_task(); // don't forget thread 0
			
	std((void*)0x200611a0,0x1ff); // restart timebase
}
	
int main(){
	LogInit();
	int i;
	int waiting_screen_ready = 0;
	unsigned long network_poll_count = 0;
	unsigned long usb_poll_count = 0;
	unsigned long last_diag_refresh_tick = 0;
	unsigned long last_gratuitous_arp_tick = 0;

	printf("ANA Dump before Init:\n");
	dumpana();

	// linux needs this
	synchronize_timebases();
	
	// irqs preinit (SMC related)
	*(volatile uint32_t*)0xea00106c = 0x1000000;
	*(volatile uint32_t*)0xea001064 = 0x10;
	*(volatile uint32_t*)0xea00105c = 0xc000000;

	xenon_smc_start_bootanim();

	// flush console after each outputted char
	setbuf(stdout,NULL);

	xenos_init(VIDEO_MODE_AUTO);

#ifdef SWIZZY_THEME
	console_set_colors(CONSOLE_COLOR_BLACK,CONSOLE_COLOR_ORANGE); // Orange text on black bg
#elif defined XTUDO_THEME
	console_set_colors(CONSOLE_COLOR_BLACK,CONSOLE_COLOR_PINK); // Pink text on black bg
#elif defined DEFAULT_THEME
	console_set_colors(CONSOLE_COLOR_BLUE,CONSOLE_COLOR_WHITE); // White text on blue bg
#else
	console_set_colors(CONSOLE_COLOR_BLACK,CONSOLE_COLOR_GREEN); // Green text on black bg
#endif
	console_init();
	xecli_branding_apply_theme();
	xecli_branding_on_initializing();

	//delay(3); //give the user a chance to see our splash screen <- network init should last long enough...

	xenon_sound_init();
	xenon_make_it_faster(XENON_SPEED_FULL);
	if (xenon_get_console_type() != REV_CORONA_PHISON) //Not needed for MMC type of consoles! ;)
	{
		sfcx_init();
		if (sfc.initialized != SFCX_INITIALIZED)
		{
			delay(5);
		}
	}

	xenon_config_init();

#ifndef NO_NETWORKING
	network_init();
	httpd_start();
	usb_init();
	{
		uint64_t network_wait_started = mftb();
		while (tb_diff_msec(mftb(), network_wait_started) < 1500)
		{
			network_poll();
			usb_do_poll();
			mdelay(10);
		}
	}
	network_print_config();
	xecli_update_network_status();

#endif

	LogDeInit();

	for(;;){
		xecli_branding_tick();
#ifndef NO_NETWORKING
		network_poll();
		network_poll_count++;
		if (waiting_screen_ready && netif.ip_addr.addr != 0 && (xecli_branding_get_heartbeat_ticks() - last_gratuitous_arp_tick) >= 200UL)
		{
			etharp_gratuitous(&netif);
			last_gratuitous_arp_tick = xecli_branding_get_heartbeat_ticks();
		}
#endif
		if (!waiting_screen_ready)
		{
			waiting_screen_ready = 1;
			xecli_update_loop_debug("wait", network_poll_count, usb_poll_count);
			xecli_branding_on_waiting_for_connection();
			last_diag_refresh_tick = xecli_branding_get_heartbeat_ticks();
			last_gratuitous_arp_tick = xecli_branding_get_heartbeat_ticks();
		}
		else if ((xecli_branding_get_heartbeat_ticks() - last_diag_refresh_tick) >= 100UL)
		{
			xecli_update_loop_debug("idle", network_poll_count, usb_poll_count);
			last_diag_refresh_tick = xecli_branding_get_heartbeat_ticks();
		}
		if (xecli_branding_should_issue_auto_reboot())
		{
			xecli_branding_mark_auto_reboot_attempted();
			xenon_smc_power_reboot();
		}
		mdelay(10);
	}

	return 0;
}

