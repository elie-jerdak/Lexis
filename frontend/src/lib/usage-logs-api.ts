
import { apiRequest } from "@/lib/api";
import type {
    UsageLogPage,
    UsageLogQuery,
} from "@/types/usage-logs";

export async function getUsageLogs(
    query: UsageLogQuery = {},
): Promise<UsageLogPage> {
    const params = new URLSearchParams();

    if (query.from) params.set("from", query.from);
    if (query.to) params.set("to", query.to);
    if (query.userId !== undefined) {
        params.set("userId", String(query.userId));
    }

    params.set("page", String(query.page ?? 1));
    params.set("pageSize", String(query.pageSize ?? 20));

    return apiRequest<UsageLogPage>(
        `/api/usage-logs?${params.toString()}`,
    );
}
