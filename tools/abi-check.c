#include <stddef.h>
#include <stdio.h>
#include "mpv/client.h"
#include "mpv/render.h"

int main(void) {
    printf("node %zu format@%zu\n", sizeof(mpv_node), offsetof(mpv_node, format));
    printf("list %zu num@%zu values@%zu keys@%zu\n", sizeof(mpv_node_list),
           offsetof(mpv_node_list, num), offsetof(mpv_node_list, values), offsetof(mpv_node_list, keys));
    printf("event %zu id@%zu err@%zu reply@%zu data@%zu\n", sizeof(mpv_event),
           offsetof(mpv_event, event_id), offsetof(mpv_event, error),
           offsetof(mpv_event, reply_userdata), offsetof(mpv_event, data));
    printf("end %zu reason@%zu error@%zu entry@%zu insert@%zu num@%zu\n", sizeof(mpv_event_end_file),
           offsetof(mpv_event_end_file, reason), offsetof(mpv_event_end_file, error),
           offsetof(mpv_event_end_file, playlist_entry_id),
           offsetof(mpv_event_end_file, playlist_insert_id),
           offsetof(mpv_event_end_file, playlist_insert_num_entries));
    printf("prop %zu name@%zu format@%zu data@%zu\n", sizeof(mpv_event_property),
           offsetof(mpv_event_property, name), offsetof(mpv_event_property, format),
           offsetof(mpv_event_property, data));
    printf("log %zu prefix@%zu level@%zu text@%zu log_level@%zu\n", sizeof(mpv_event_log_message),
           offsetof(mpv_event_log_message, prefix), offsetof(mpv_event_log_message, level),
           offsetof(mpv_event_log_message, text), offsetof(mpv_event_log_message, log_level));
    printf("param %zu type@%zu data@%zu\n", sizeof(mpv_render_param),
           offsetof(mpv_render_param, type), offsetof(mpv_render_param, data));
    return 0;
}
