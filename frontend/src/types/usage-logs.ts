
export interface UsageLogItem {
    id: string;
    userId: number;
    messageId: string | null;
    requestTime: string;
    responseTimeMs: number;
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    cacheHit: boolean;
}

export interface UsageLogSummary {
    totalRequests: number;
    totalInputTokens: number;
    totalOutputTokens: number;
    totalTokens: number;
    averageResponseTimeMs: number;
    cacheHits: number;
    cacheHitRate: number;
}

export interface UsageLogPage {
    items: UsageLogItem[];
    summary: UsageLogSummary;
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
}

export interface UsageLogQuery {
    from?: string;
    to?: string;
    userId?: number;
    page?: number;
    pageSize?: number;
}
