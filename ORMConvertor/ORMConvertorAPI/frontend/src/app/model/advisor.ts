import { SourceUnit } from "./convert";
import { ORMType } from "./orm-type";

export interface AdvisorRunQuery {
  id: string;
  query: SourceUnit;
  weight: number;
}

export interface AdvisorRunRequest {
  sourceOrm: ORMType;
  entities: SourceUnit[];
  queries: AdvisorRunQuery[];
  maxMemoryBytes: number;
  maxFrameworksToSelect: number;
  targetFrameworks?: ORMType[];
}

export interface BenchmarkMeasurementDto {
  meanDurationMilliseconds: number;
  allocatedBytes: number;
}

export type MeasurementsByFramework = Record<ORMType, BenchmarkMeasurementDto>;
export type AdvisorMeasurements = Record<string, MeasurementsByFramework>;

export interface AdvisorRunResult {
  selectedFrameworks: ORMType[];
  queryAssignments: Record<string, ORMType>;
  measurements: AdvisorMeasurements;
}
