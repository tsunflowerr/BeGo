"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import {
  clampBenchmarkScenarioCount,
  formatCompactDuration,
  formatMetricPercent,
  signedGapPercent,
} from "@/lib/frontend-utils";
import {
  BenchmarkAlgorithmRun,
  BenchmarkScenarioResult,
  OutingBenchmarkReport,
} from "@/types";

function ms(value: number) {
  if (!Number.isFinite(value)) return "-";
  return value < 1000 ? `${Math.round(value)}ms` : `${(value / 1000).toFixed(1)}s`;
}

function metricSeconds(value: number) {
  if (!Number.isFinite(value)) return "-";
  return formatCompactDuration(value);
}

function optigoRun(scenario: BenchmarkScenarioResult): BenchmarkAlgorithmRun | undefined {
  return scenario.runs.find((run) => run.isOptiGo);
}

export default function BenchmarkPage() {
  const [seed, setSeed] = useState(20260505);
  const [scenarioCount, setScenarioCount] = useState(18);
  const [report, setReport] = useState<OutingBenchmarkReport | null>(null);
  const [selectedScenarioId, setSelectedScenarioId] = useState<string | null>(null);
  const [isRunning, setIsRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const autoRunRef = useRef(false);

  const runBenchmark = useCallback(async () => {
    setIsRunning(true);
    setError(null);
    try {
      const next = await api.benchmarks.runOuting(seed, clampBenchmarkScenarioCount(scenarioCount));
      setReport(next);
      setSelectedScenarioId(next.weaknesses[0]?.scenarioId ?? next.scenarios[0]?.scenarioId ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to run benchmark.");
    } finally {
      setIsRunning(false);
    }
  }, [scenarioCount, seed]);

  useEffect(() => {
    if (autoRunRef.current) return;
    autoRunRef.current = true;
    void runBenchmark();
  }, [runBenchmark]);

  const selectedScenario = useMemo(
    () => report?.scenarios.find((scenario) => scenario.scenarioId === selectedScenarioId) ?? report?.scenarios[0] ?? null,
    [report, selectedScenarioId]
  );
  const optigoAggregate = report?.aggregates.find((aggregate) => aggregate.isOptiGo);
  const weakScenarioIds = new Set(report?.weaknesses.map((weakness) => weakness.scenarioId) ?? []);

  return (
    <main className="min-h-screen px-4 py-5 text-[#172033] sm:px-6 lg:px-8">
      <div className="mx-auto flex max-w-[1500px] flex-col gap-5">
        <header className="bego-hard-card flex flex-col gap-4 bg-white px-4 py-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex flex-wrap items-center gap-3">
            <Link href="/" className="grid h-11 w-11 place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] text-lg font-black shadow-[3px_3px_0_#172033]">
              B
            </Link>
            <div>
              <p className="text-xs font-black uppercase text-[#64748b]">BeGo benchmark lab</p>
              <h1 className="text-2xl font-black">Outing optimization report</h1>
            </div>
            {report && <span className="bego-chip bg-[#45d483]">{report.scenarioCount} scenarios</span>}
            {report && <span className="bego-chip bg-[#48c7df]">Runtime {ms(report.totalRuntimeMs)}</span>}
          </div>

          <form
            className="flex flex-wrap items-end gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              void runBenchmark();
            }}
          >
            <label className="grid gap-1 text-sm font-black">
              Seed
              <input className="bego-input h-11 min-h-11 w-36" type="number" value={seed} onChange={(event) => setSeed(Number(event.target.value))} />
            </label>
            <label className="grid gap-1 text-sm font-black">
              Scenarios
              <input
                className="bego-input h-11 min-h-11 w-32"
                min={1}
                max={60}
                type="number"
                value={scenarioCount}
                onChange={(event) => setScenarioCount(clampBenchmarkScenarioCount(Number(event.target.value)))}
              />
            </label>
            <button className="bego-primary" type="submit" disabled={isRunning}>
              {isRunning ? "Running..." : "Run benchmark"}
            </button>
          </form>
        </header>

        {error && <div className="rounded-2xl border-2 border-[#d42712] bg-[#fff1f2] p-4 font-bold text-[#b42318]">{error}</div>}

        {report && optigoAggregate && (
          <section className="grid gap-4 md:grid-cols-4">
            <Kpi label="Cost gap" value={signedGapPercent(optigoAggregate.averageCostGapToBestExternalPercent)} tone="bg-[#f7c948]" />
            <Kpi label="Fairness gain" value={signedGapPercent(optigoAggregate.averageFairnessGainVsBestCostExternalPercent)} tone="bg-[#45d483]" />
            <Kpi label="Worst burden" value={metricSeconds(optiGoOrZero(optigoAggregate.averageMaxMemberBurdenSeconds))} tone="bg-[#f472b6]" />
            <Kpi label="Driver Gini" value={optigoAggregate.averageDriverDetourGini.toFixed(2)} tone="bg-[#48c7df]" />
          </section>
        )}

        {!report && !error && (
          <section className="bego-hard-card grid place-items-center bg-white p-12 text-center">
            <h2 className="text-3xl font-black">Benchmark is starting</h2>
            <p className="mt-2 font-semibold text-[#64748b]">Comparing OptiGo with median, greedy insertion and exact doorstep VRP.</p>
          </section>
        )}

        {report && (
          <section className="grid gap-5 xl:grid-cols-[minmax(0,1.2fr)_420px]">
            <AggregateTable report={report} />
            <WeaknessPanel report={report} selectedScenarioId={selectedScenarioId} onSelect={setSelectedScenarioId} />
          </section>
        )}

        {report && selectedScenario && (
          <section className="grid gap-5 xl:grid-cols-[340px_minmax(0,1fr)]">
            <ScenarioList
              scenarios={report.scenarios}
              selectedScenarioId={selectedScenario.scenarioId}
              weakScenarioIds={weakScenarioIds}
              onSelect={setSelectedScenarioId}
            />
            <ScenarioDetail scenario={selectedScenario} sources={report.sources} />
          </section>
        )}
      </div>
    </main>
  );
}

function optiGoOrZero(value: number) {
  return Number.isFinite(value) ? value : 0;
}

function Kpi({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <div className={`bego-hard-card p-4 ${tone}`}>
      <p className="text-xs font-black uppercase text-[#172033]/70">{label}</p>
      <p className="mt-2 text-3xl font-black">{value}</p>
    </div>
  );
}

function AggregateTable({ report }: { report: OutingBenchmarkReport }) {
  return (
    <section className="bego-hard-card overflow-hidden bg-white">
      <div className="border-b-2 border-[#172033] p-4">
        <h2 className="text-xl font-black">Algorithm aggregate</h2>
      </div>
      <div className="bego-scrollbar overflow-x-auto">
        <table className="w-full min-w-[1320px] text-left text-sm">
          <thead className="bg-[#fff7dc] text-xs uppercase">
            <tr>
              {["Algorithm", "Feasible", "Svc feasible", "Win", "Objective", "Pure cost", "Cost gap", "Fair score", "Fair gain", "Passenger", "Burden", "Regret", "Detour", "Gini", "Stops", "Shared", "Runtime"].map((head) => (
                <th key={head} className="border-b-2 border-[#172033] px-4 py-3 font-black">{head}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {report.aggregates.map((aggregate) => (
              <tr key={aggregate.algorithmKey} className="border-b border-[#d8e3ea]">
                <td className="px-4 py-3 font-black">
                  {aggregate.algorithmName}
                  {aggregate.isOptiGo && <span className="ml-2 rounded-full bg-[#ff3b1f] px-2 py-1 text-[10px] font-black text-white">OptiGo</span>}
                </td>
                <td className="px-4 py-3">{formatMetricPercent(aggregate.feasibleRate)}</td>
                <td className="px-4 py-3">{formatMetricPercent(aggregate.serviceableFeasibleRate)}</td>
                <td className="px-4 py-3">{formatMetricPercent(aggregate.winRate)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageObjectiveSeconds)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averagePureCostSeconds)}</td>
                <td className="px-4 py-3">{signedGapPercent(aggregate.averageCostGapToBestExternalPercent)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageFairnessScoreSeconds)}</td>
                <td className="px-4 py-3">{signedGapPercent(aggregate.averageFairnessGainVsBestCostExternalPercent)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageMaxPassengerTimeSeconds)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageMaxMemberBurdenSeconds)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageWorstMemberRegretSeconds)}</td>
                <td className="px-4 py-3">{metricSeconds(aggregate.averageMaxDriverDetourSeconds)}</td>
                <td className="px-4 py-3">{aggregate.averageDriverDetourGini.toFixed(2)}</td>
                <td className="px-4 py-3">{aggregate.averageStopCount.toFixed(1)}</td>
                <td className="px-4 py-3">{formatMetricPercent(aggregate.averageSharedStopRate)}</td>
                <td className="px-4 py-3">{ms(aggregate.averageComputeTimeMs)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function WeaknessPanel({ report, selectedScenarioId, onSelect }: { report: OutingBenchmarkReport; selectedScenarioId: string | null; onSelect: (id: string) => void }) {
  return (
    <section className="bego-hard-card bg-white p-4">
      <h2 className="text-xl font-black">Weak cases</h2>
      <div className="bego-scrollbar mt-4 grid max-h-[430px] gap-3 overflow-y-auto pr-1">
        {report.weaknesses.length === 0 && <p className="font-semibold text-[#64748b]">No weakness above threshold in this run.</p>}
        {report.weaknesses.map((weakness) => (
          <button
            key={`${weakness.scenarioId}-${weakness.metric}-${weakness.bestExternalAlgorithm}`}
            type="button"
            onClick={() => onSelect(weakness.scenarioId)}
            className={`rounded-2xl border-2 border-[#172033] p-3 text-left shadow-[3px_3px_0_#d8e3ea] ${selectedScenarioId === weakness.scenarioId ? "bg-[#fff7dc]" : "bg-white"}`}
          >
            <div className="flex items-center justify-between gap-3">
              <span className="font-black">{weakness.scenarioId}</span>
              <span className="bego-chip min-h-7 bg-[#f7c948]">{signedGapPercent(weakness.gapPercent)}</span>
            </div>
            <p className="mt-2 text-sm font-bold text-[#475569]">{weakness.message}</p>
            <p className="mt-1 text-xs font-bold text-[#64748b]">Best external: {weakness.bestExternalAlgorithm}</p>
          </button>
        ))}
      </div>
    </section>
  );
}

function ScenarioList({
  scenarios,
  selectedScenarioId,
  weakScenarioIds,
  onSelect,
}: {
  scenarios: BenchmarkScenarioResult[];
  selectedScenarioId: string;
  weakScenarioIds: Set<string>;
  onSelect: (id: string) => void;
}) {
  return (
    <section className="bego-card bg-white p-4">
      <h2 className="text-xl font-black">Scenarios</h2>
      <div className="bego-scrollbar mt-4 grid max-h-[620px] gap-3 overflow-y-auto pr-1">
        {scenarios.map((scenario) => {
          const optigo = optigoRun(scenario);
          return (
            <button
              key={scenario.scenarioId}
              type="button"
              onClick={() => onSelect(scenario.scenarioId)}
              className={`rounded-2xl border-2 border-[#172033] p-3 text-left ${selectedScenarioId === scenario.scenarioId ? "bg-[#45d483]" : "bg-white"}`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="font-black">{scenario.scenarioId}</span>
                {weakScenarioIds.has(scenario.scenarioId) && <span className="bego-chip min-h-7 bg-[#f7c948]">weak</span>}
                {!scenario.isServiceable && <span className="bego-chip min-h-7 bg-[#f472b6]">capacity</span>}
              </div>
              <p className="mt-1 text-xs font-black uppercase text-[#64748b]">{scenario.layout}</p>
              <p className="mt-1 text-sm font-bold text-[#475569]">
                {scenario.memberCount} members - {scenario.pickupPassengerCount} pickup - {scenario.driverCount} drivers
              </p>
              {optigo && <p className="mt-1 text-xs font-bold text-[#64748b]">Cost {signedGapPercent(optigo.costGapToBestExternalPercent)} - Fair {signedGapPercent(optigo.fairnessGainVsBestCostExternalPercent)}</p>}
            </button>
          );
        })}
      </div>
    </section>
  );
}

function ScenarioDetail({ scenario, sources }: { scenario: BenchmarkScenarioResult; sources: OutingBenchmarkReport["sources"] }) {
  return (
    <section className="bego-hard-card overflow-hidden bg-white">
      <div className="border-b-2 border-[#172033] p-4">
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <span className="bego-chip bg-[#48c7df]">{scenario.layout}</span>
            {!scenario.isServiceable && <span className="ml-2 bego-chip bg-[#f472b6]">unserviceable</span>}
            <h2 className="mt-3 text-2xl font-black">{scenario.scenarioId}</h2>
            <p className="mt-1 font-semibold text-[#64748b]">{scenario.description}</p>
            {scenario.unserviceableReason && <p className="mt-1 text-sm font-bold text-[#b42318]">{scenario.unserviceableReason}</p>}
          </div>
          <span className="bego-chip bg-[#f7c948]">{scenario.venueCount} venues</span>
        </div>
      </div>

      <div className="bego-scrollbar overflow-x-auto">
        <table className="w-full min-w-[1420px] text-left text-sm">
          <thead className="bg-[#fff7dc] text-xs uppercase">
            <tr>
              {["Algorithm", "Venue", "Feasible", "Obj gap", "Cost gap", "Fair gain", "Objective", "Pure cost", "Fair score", "Group", "Passenger", "Burden", "Regret", "Detour", "Gini", "Stops", "Shared", "Walk", "Runtime"].map((head) => (
                <th key={head} className="border-b-2 border-[#172033] px-4 py-3 font-black">{head}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {scenario.runs
              .slice()
              .sort((a, b) => a.objectiveSeconds - b.objectiveSeconds)
              .map((run) => (
                <tr key={run.algorithmKey} className="border-b border-[#d8e3ea]">
                  <td className="px-4 py-3 font-black">
                    {run.algorithmName}
                    {run.isOptiGo && <span className="ml-2 rounded-full bg-[#ff3b1f] px-2 py-1 text-[10px] font-black text-white">OptiGo</span>}
                  </td>
                  <td className="px-4 py-3">{run.selectedVenueName}</td>
                  <td className="px-4 py-3">{run.isFeasible ? "yes" : "no"}</td>
                  <td className="px-4 py-3">{signedGapPercent(run.gapToBestExternalPercent)}</td>
                  <td className="px-4 py-3">{signedGapPercent(run.costGapToBestExternalPercent)}</td>
                  <td className="px-4 py-3">{signedGapPercent(run.fairnessGainVsBestCostExternalPercent)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.objectiveSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.pureCostSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.fairnessScoreSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.totalGroupTimeSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.maxPassengerTimeSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.maxMemberBurdenSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.worstMemberRegretSeconds)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.maxDriverDetourSeconds)}</td>
                  <td className="px-4 py-3">{run.driverDetourGini.toFixed(2)}</td>
                  <td className="px-4 py-3">{run.stopCount}</td>
                  <td className="px-4 py-3">{formatMetricPercent(run.sharedStopRate)}</td>
                  <td className="px-4 py-3">{metricSeconds(run.maxWalkingTimeSeconds)}</td>
                  <td className="px-4 py-3">{ms(run.computeTimeMs)}</td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>

      <div className="grid gap-4 border-t-2 border-[#172033] p-4 lg:grid-cols-2">
        <div>
          <h3 className="text-lg font-black">Input members</h3>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            {scenario.members.map((member) => (
              <div key={`${scenario.scenarioId}-${member.name}`} className="rounded-2xl border-2 border-[#172033] bg-[#f6fcff] p-3">
                <p className="font-black">{member.name}</p>
                <p className="mt-1 text-xs font-bold text-[#64748b]">{member.role} - {member.transportMode} - seats {member.seatCapacity}</p>
              </div>
            ))}
          </div>
        </div>
        <div>
          <h3 className="text-lg font-black">Benchmark design sources</h3>
          <div className="mt-3 grid gap-2">
            {sources.map((source) => (
              <a key={source.url} href={source.url} target="_blank" rel="noreferrer" className="rounded-2xl border-2 border-[#172033] bg-[#fff7dc] p-3">
                <p className="font-black">{source.label}</p>
                <p className="mt-1 text-xs font-bold text-[#64748b]">{source.relevance}</p>
              </a>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
