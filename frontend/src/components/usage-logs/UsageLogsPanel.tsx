"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiRequest } from "@/lib/api";
import { getUsageLogs } from "@/lib/usage-logs-api";

import type {
    UsageLogItem,
    UsageLogPage,
    UsageLogQuery,
} from "@/types/usage-logs";

import {Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer,
    Tooltip, XAxis, YAxis,
} from "recharts";

const PAGE_SIZE = 10;

function getDateString(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

function formatDate(value: string): string {
    return new Date(value).toLocaleString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
    });
}

function formatNumber(value: number): string {
    return new Intl.NumberFormat().format(value);
}

function formatDuration(value: number): string {
    if (value < 1000) return `${Math.round(value)} ms`;
    return `${(value / 1000).toFixed(2)} s`;
}

function StatCard({label, value, detail}: {
    label: string;
    value: string;
    detail: string;
}) {
    return (
        <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
            <p className="text-sm font-medium text-slate-500">{label}</p>
            <p className="mt-4 text-3xl font-semibold tracking-tight text-slate-900">
                {value}
            </p>
            <p className="mt-1 text-xs text-slate-400">{detail}</p>
        </div>
    );
}

export default function UsageLogsPanel() {
    const [data, setData] = useState<UsageLogPage | null>(null);
    const [from, setFrom] = useState(() => {
        const date = new Date();
        date.setDate(date.getDate() - 30);
        return getDateString(date);
    });
    const [to, setTo] = useState(() => getDateString(new Date()));
    const [userIdInput, setUserIdInput] = useState("");
    const [appliedUserId, setAppliedUserId] = useState<number | undefined>();
    const [page, setPage] = useState(1);
    const [isAdmin, setIsAdmin] = useState(false);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState("");

    const loadLogs = useCallback(async () => {
        setIsLoading(true);
        setError("");

        try {
            const query: UsageLogQuery = {
                from: from
                    ? new Date(`${from}T00:00:00`).toISOString()
                    : undefined,
                to: to
                    ? new Date(`${to}T23:59:59.999`).toISOString()
                    : undefined,
                page,
                pageSize: PAGE_SIZE,
            };

            if (isAdmin && appliedUserId !== undefined) {
                query.userId = appliedUserId;
            }

            const result = await getUsageLogs(query);
            setData(result);
        } catch (err) {
            setError(
                err instanceof Error
                    ? err.message
                    : "Could not load usage logs.",
            );
        } finally {
            setIsLoading(false);
        }
    }, [from, to, page, isAdmin, appliedUserId]);

    useEffect(() => {
        void loadLogs();
    }, [loadLogs]);

    useEffect(() => {
    let cancelled = false;

    async function loadSession() {
        try {
            const session = await apiRequest<{
                authenticated: boolean;
                isAdmin: boolean;
            }>("/api/Auth/session");

            if (!cancelled) {
                setIsAdmin(session.isAdmin);
            }
        } catch {
            if (!cancelled) {
                setIsAdmin(false);
            }
        }
    }

    void loadSession();

        return () => {
            cancelled = true;
        };
    }, []);

    function applyFilters(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();

        if (from && to && from > to) {
            setError("The start date must not be later than the end date.");
            return;
        }

        if (
            isAdmin &&
            userIdInput.trim() &&
            (!Number.isInteger(Number(userIdInput)) ||
                Number(userIdInput) <= 0)
        ) {
            setError("Enter a valid positive user ID.");
            return;
        }

        setError("");
        setAppliedUserId(
            isAdmin && userIdInput.trim()
                ? Number(userIdInput)
                : undefined,
        );
        setPage(1);
    }

    function clearFilters() {
        const date = new Date();
        const start = new Date();
        start.setDate(start.getDate() - 30);

        setFrom(getDateString(start));
        setTo(getDateString(date));
        setUserIdInput("");
        setAppliedUserId(undefined);
        setPage(1);
        setError("");
    }

    const summary = data?.summary;

    const dailyUsage = useMemo(() => {
        const grouped = new Map<
            string,
            {
                date: string;
                label: string;
                requests: number;
                inputTokens: number;
                outputTokens: number;
                responseTimeTotal: number;
            }
        >();

        for (const log of data?.items ?? []) {
            const date = new Date(log.requestTime);
            const key = [
                date.getFullYear(),
                String(date.getMonth() + 1).padStart(2, "0"),
                String(date.getDate()).padStart(2, "0"),
            ].join("-");

            const existing = grouped.get(key) ?? {
                date: key,
                label: date.toLocaleDateString(undefined, {
                    month: "short",
                    day: "numeric",
                }),
                requests: 0,
                inputTokens: 0,
                outputTokens: 0,
                responseTimeTotal: 0,
            };

            existing.requests += 1;
            existing.inputTokens += log.inputTokens;
            existing.outputTokens += log.outputTokens;
            existing.responseTimeTotal += log.responseTimeMs;

            grouped.set(key, existing);
        }

        return Array.from(grouped.values())
            .sort((a, b) => a.date.localeCompare(b.date))
            .map((day) => ({
                ...day,
                averageResponseTime:
                    day.requests > 0
                        ? Math.round(day.responseTimeTotal / day.requests)
                        : 0,
            }));
    }, [data?.items]);

    const tokenDistribution = [
        { name: "Input tokens", value: summary?.totalInputTokens ?? 0 },
        { name: "Output tokens", value: summary?.totalOutputTokens ?? 0 },
    ];

    const cacheDistribution = [
        { name: "Cache hits", value: summary?.cacheHits ?? 0 },
        {
            name: "Cache misses",
            value: Math.max(
                0,
                (summary?.totalRequests ?? 0) - (summary?.cacheHits ?? 0),
            ),
        },
    ];

    return (
        <div className="space-y-8">
            <div>
                <h1 className="text-[28px] font-semibold tracking-tight text-slate-900 sm:text-[32px]">
                    Usage Logs
                </h1>
                <p className="mt-2 text-sm leading-6 text-slate-500">
                    Monitor request activity, token consumption, response times,
                    and cache performance.
                </p>
            </div>

            <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <StatCard
                    label="Total requests"
                    value={formatNumber(summary?.totalRequests ?? 0)}
                    detail="Within the selected period"
                />
                <StatCard
                    label="Total tokens"
                    value={formatNumber(summary?.totalTokens ?? 0)}
                    detail={`${formatNumber(summary?.totalInputTokens ?? 0)} input · ${formatNumber(summary?.totalOutputTokens ?? 0)} output`}
                />
                <StatCard
                    label="Avg. response time"
                    value={formatDuration(summary?.averageResponseTimeMs ?? 0)}
                    detail="Across matching requests"
                />
                <StatCard
                    label="Cache hit rate"
                    value={`${(summary?.cacheHitRate ?? 0).toFixed(1)}%`}
                    detail={`${formatNumber(summary?.cacheHits ?? 0)} cache hits`}
                />
            </section>

            <section className="space-y-4">
                <div>
                    <h2 className="text-lg font-semibold tracking-tight text-slate-900">
                        Usage Analytics
                    </h2>
                    <p className="mt-1 text-sm text-slate-500">
                        Visualize request activity, token consumption, and cache performance.
                    </p>
                </div>

                <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
                    {/* Requests per day */}
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                        <h3 className="font-semibold text-slate-900">
                            Requests per day
                        </h3>
                        <p className="mt-1 text-xs text-slate-500">
                            Daily request counts for the loaded records
                        </p>

                        <div className="mt-5 h-64">
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart
                                    data={dailyUsage}
                                    margin={{ top: 8, right: 8, left: -16, bottom: 0 }}
                                >
                                    <CartesianGrid
                                        strokeDasharray="3 3"
                                        vertical={false}
                                        stroke="#e2e8f0"
                                    />
                                    <XAxis
                                        dataKey="label"
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                    />
                                    <YAxis
                                        allowDecimals={false}
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                    />
                                    <Tooltip />
                                    <Bar
                                        dataKey="requests"
                                        name="Requests"
                                        fill="#7c3aed"
                                        radius={[5, 5, 0, 0]}
                                    />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>
                    </div>

                    {/* Token usage over time */}
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                        <h3 className="font-semibold text-slate-900">
                            Token consumption
                        </h3>
                        <p className="mt-1 text-xs text-slate-500">
                            Input versus output tokens by day
                        </p>

                        <div className="mt-5 h-64">
                            <ResponsiveContainer width="100%" height="100%">
                                <LineChart
                                    data={dailyUsage}
                                    margin={{ top: 8, right: 12, left: -8, bottom: 0 }}
                                >
                                    <CartesianGrid
                                        strokeDasharray="3 3"
                                        vertical={false}
                                        stroke="#e2e8f0"
                                    />
                                    <XAxis
                                        dataKey="label"
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                    />
                                    <YAxis
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                        tickFormatter={(value) =>
                                            new Intl.NumberFormat("en", {
                                                notation: "compact",
                                            }).format(value)
                                        }
                                    />
                                    <Tooltip
                                        formatter={(value) => formatNumber(Number(value))}
                                    />
                                    <Legend />
                                    <Line
                                        type="monotone"
                                        dataKey="inputTokens"
                                        name="Input tokens"
                                        stroke="#7c3aed"
                                        strokeWidth={2.5}
                                        dot={false}
                                        activeDot={{ r: 5 }}
                                    />
                                    <Line
                                        type="monotone"
                                        dataKey="outputTokens"
                                        name="Output tokens"
                                        stroke="#0d9488"
                                        strokeWidth={2.5}
                                        dot={false}
                                        activeDot={{ r: 5 }}
                                    />
                                </LineChart>
                            </ResponsiveContainer>
                        </div>
                    </div>

                    {/* Response time */}
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                        <h3 className="font-semibold text-slate-900">
                            Average response time
                        </h3>
                        <p className="mt-1 text-xs text-slate-500">
                            Average duration per day, in seconds
                        </p>

                        <div className="mt-5 h-64">
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart
                                    data={dailyUsage}
                                    margin={{ top: 8, right: 8, left: -8, bottom: 0 }}
                                >
                                    <CartesianGrid
                                        strokeDasharray="3 3"
                                        vertical={false}
                                        stroke="#e2e8f0"
                                    />
                                    <XAxis
                                        dataKey="label"
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                    />
                                    <YAxis
                                        tick={{ fontSize: 12, fill: "#64748b" }}
                                        axisLine={false}
                                        tickLine={false}
                                        tickFormatter={(value) => `${(value / 1000).toFixed(0)}s`}
                                    />
                                    <Tooltip
                                        formatter={(value) =>
                                            formatDuration(Number(value))
                                        }
                                    />
                                    <Bar
                                        dataKey="averageResponseTime"
                                        name="Average response time"
                                        fill="#0d9488"
                                        radius={[5, 5, 0, 0]}
                                    />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>
                    </div>

                    {/* Cache performance */}
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                        <h3 className="font-semibold text-slate-900">
                            Cache performance
                        </h3>
                        <p className="mt-1 text-xs text-slate-500">
                            Cache hits versus misses across matching requests
                        </p>

                        <div className="mt-5 h-64">
                            {(summary?.totalRequests ?? 0) === 0 ? (
                                <div className="flex h-full items-center justify-center text-sm text-slate-400">
                                    No cache data available.
                                </div>
                            ) : (
                                <ResponsiveContainer width="100%" height="100%">
                                    <PieChart>
                                        <Pie
                                            data={cacheDistribution}
                                            dataKey="value"
                                            nameKey="name"
                                            cx="50%"
                                            cy="45%"
                                            innerRadius={62}
                                            outerRadius={92}
                                            paddingAngle={3}
                                        >
                                            {cacheDistribution.map((entry, index) => (
                                                <Cell
                                                    key={entry.name}
                                                    fill={
                                                        index === 0
                                                            ? "#0d9488"
                                                            : "#cbd5e1"
                                                    }
                                                />
                                            ))}
                                        </Pie>
                                        <Tooltip
                                            formatter={(value) =>
                                                formatNumber(Number(value))
                                            }
                                        />
                                        <Legend verticalAlign="bottom" />
                                    </PieChart>
                                </ResponsiveContainer>
                            )}
                        </div>

                        <p className="text-center text-sm text-slate-500">
                            Hit rate:{" "}
                            <span className="font-semibold text-slate-900">
                                {(summary?.cacheHitRate ?? 0).toFixed(1)}%
                            </span>
                        </p>
                    </div>

                    {/* Token distribution */}
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30 xl:col-span-2">
                        <h3 className="font-semibold text-slate-900">
                            Overall token distribution
                        </h3>
                        <p className="mt-1 text-xs text-slate-500">
                            Input and output token totals for the selected filters
                        </p>

                        <div className="mt-4 grid grid-cols-1 items-center gap-4 sm:grid-cols-2">
                            <div className="h-56">
                                <ResponsiveContainer width="100%" height="100%">
                                    <PieChart>
                                        <Pie
                                            data={tokenDistribution}
                                            dataKey="value"
                                            nameKey="name"
                                            cx="50%"
                                            cy="50%"
                                            innerRadius={58}
                                            outerRadius={85}
                                            paddingAngle={3}
                                        >
                                            {tokenDistribution.map((entry, index) => (
                                                <Cell
                                                    key={entry.name}
                                                    fill={
                                                        index === 0
                                                            ? "#7c3aed"
                                                            : "#0d9488"
                                                    }
                                                />
                                            ))}
                                        </Pie>
                                        <Tooltip
                                            formatter={(value) =>
                                                formatNumber(Number(value))
                                            }
                                        />
                                        <Legend />
                                    </PieChart>
                                </ResponsiveContainer>
                            </div>

                            <div className="space-y-4">
                                <div className="rounded-xl bg-violet-50 p-4">
                                    <p className="text-sm text-violet-700">
                                        Input tokens
                                    </p>
                                    <p className="mt-1 text-2xl font-semibold text-violet-950">
                                        {formatNumber(summary?.totalInputTokens ?? 0)}
                                    </p>
                                </div>
                                <div className="rounded-xl bg-teal-50 p-4">
                                    <p className="text-sm text-teal-700">
                                        Output tokens
                                    </p>
                                    <p className="mt-1 text-2xl font-semibold text-teal-950">
                                        {formatNumber(summary?.totalOutputTokens ?? 0)}
                                    </p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            <section className="overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-sm shadow-slate-200/30">
                <form
                    onSubmit={applyFilters}
                    className="flex flex-col gap-4 border-b border-slate-100 p-5 sm:p-6"
                >
                    <div>
                        <h2 className="text-base font-semibold text-slate-900">
                            Request history
                        </h2>
                        <p className="mt-1 text-xs text-slate-400">
                            Filter requests by date and review their usage details.
                        </p>
                    </div>

                    <div className="flex flex-col items-end gap-3 lg:flex-row">
                        <label className="w-full flex-1 text-xs font-medium text-slate-600">
                            From
                            <input
                                type="date"
                                value={from}
                                max={to || undefined}
                                onChange={(event) => setFrom(event.target.value)}
                                className="mt-1.5 w-full rounded-lg border border-slate-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-violet-300 focus:ring-4 focus:ring-violet-50"
                            />
                        </label>

                        <label className="w-full flex-1 text-xs font-medium text-slate-600">
                            To
                            <input
                                type="date"
                                value={to}
                                min={from || undefined}
                                max={getDateString(new Date())}
                                onChange={(event) => setTo(event.target.value)}
                                className="mt-1.5 w-full rounded-lg border border-slate-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-violet-300 focus:ring-4 focus:ring-violet-50"
                            />
                        </label>

                        {isAdmin && (
                            <label className="w-full flex-1 text-xs font-medium text-slate-600">
                                User ID
                                <input
                                    type="number"
                                    min={1}
                                    value={userIdInput}
                                    onChange={(event) => setUserIdInput(event.target.value)}
                                    placeholder="All users"
                                    className="mt-1.5 w-full rounded-lg border border-slate-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-violet-300 focus:ring-4 focus:ring-violet-50"
                                />
                            </label>
                        )}

                        <div className="flex w-full shrink-0 gap-2 lg:w-auto">
                            <button
                                type="submit"
                                disabled={isLoading}
                                className="flex-1 rounded-lg bg-violet-600 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-violet-700 disabled:opacity-50 lg:flex-none"
                            >
                                Apply
                            </button>
                            <button
                                type="button"
                                onClick={clearFilters}
                                className="flex-1 rounded-lg border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-600 transition hover:bg-slate-50 lg:flex-none"
                            >
                                Reset
                            </button>
                        </div>
                    </div>
                </form>

                {error && (
                    <div
                        role="alert"
                        className="m-5 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
                    >
                        {error}
                        <button
                            type="button"
                            onClick={() => void loadLogs()}
                            className="ml-3 font-semibold underline"
                        >
                            Retry
                        </button>
                    </div>
                )}

                <div className="overflow-x-auto">
                    <table className="w-full min-w-[900px] text-left">
                        <thead>
                            <tr className="bg-slate-50/70 text-[10px] font-semibold uppercase tracking-[0.13em] text-slate-400">
                                {isAdmin && (
                                    <th className="px-5 py-3.5">User ID</th>
                                )}
                                <th className="px-5 py-3.5">Request time</th>
                                <th className="px-4 py-3.5">Duration</th>
                                <th className="px-4 py-3.5">Input tokens</th>
                                <th className="px-4 py-3.5">Output tokens</th>
                                <th className="px-4 py-3.5">Total tokens</th>
                                <th className="px-4 py-3.5">Cache</th>
                                <th className="px-5 py-3.5">Message ID</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                            {isLoading && (
                                <tr>
                                    <td
                                        colSpan={isAdmin ? 8 : 7}
                                        className="px-6 py-14 text-center text-sm text-slate-500"
                                    >
                                        Loading usage logs...
                                    </td>
                                </tr>
                            )}

                            {!isLoading &&
                                !error &&
                                data?.items.map((log: UsageLogItem) => (
                                    <tr key={log.id} className="transition hover:bg-slate-50/70">
                                        {isAdmin && (
                                            <td className="px-5 py-4 text-sm text-slate-600">
                                                {log.userId}
                                            </td>
                                        )}
                                        <td className="whitespace-nowrap px-5 py-4 text-sm text-slate-700">
                                            {formatDate(log.requestTime)}
                                        </td>
                                        <td className="whitespace-nowrap px-4 py-4 text-sm text-slate-600">
                                            {formatDuration(log.responseTimeMs)}
                                        </td>
                                        <td className="px-4 py-4 text-sm text-slate-600">
                                            {formatNumber(log.inputTokens)}
                                        </td>
                                        <td className="px-4 py-4 text-sm text-slate-600">
                                            {formatNumber(log.outputTokens)}
                                        </td>
                                        <td className="px-4 py-4 text-sm font-medium text-slate-800">
                                            {formatNumber(log.totalTokens)}
                                        </td>
                                        <td className="px-4 py-4">
                                            <span
                                                className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${log.cacheHit
                                                        ? "bg-emerald-50 text-emerald-700"
                                                        : "bg-slate-100 text-slate-500"
                                                    }`}
                                            >
                                                {log.cacheHit ? "Hit" : "Miss"}
                                            </span>
                                        </td>
                                        <td
                                            className="max-w-[180px] truncate px-5 py-4 font-mono text-xs text-slate-500"
                                            title={log.messageId ?? "No associated message"}
                                        >
                                            {log.messageId ?? "—"}
                                        </td>
                                    </tr>
                                ))}

                            {!isLoading && !error && (data?.items.length ?? 0) === 0 && (
                                <tr>
                                    <td
                                        colSpan={isAdmin ? 8 : 7}
                                        className="px-6 py-14 text-center text-sm text-slate-500"
                                    >
                                        No usage logs found for this period.
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>

                <div className="flex flex-col gap-3 border-t border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6">
                    <p className="text-xs text-slate-500">
                        {data
                            ? `Showing ${data.items.length ? (data.page - 1) * data.pageSize + 1 : 0}–${(data.page - 1) * data.pageSize + data.items.length} of ${formatNumber(data.totalCount)} requests`
                            : " "}
                    </p>

                    <div className="flex items-center gap-2">
                        <button
                            type="button"
                            disabled={isLoading || page <= 1}
                            onClick={() => setPage((current) => current - 1)}
                            className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                        >
                            Previous
                        </button>
                        <span className="px-2 text-xs text-slate-500">
                            Page {data?.page ?? page} of {data?.totalPages ?? 0}
                        </span>
                        <button
                            type="button"
                            disabled={
                                isLoading ||
                                page >= (data?.totalPages ?? 0)
                            }
                            onClick={() => setPage((current) => current + 1)}
                            className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                        >
                            Next
                        </button>
                    </div>
                </div>
            </section>
        </div>
    );
}
