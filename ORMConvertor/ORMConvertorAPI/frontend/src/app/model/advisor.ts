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

export interface AdvisorRunResult {
  objective: number;
  selectedFrameworks: ORMType[];
  queryAssignments: Record<string, ORMType>;
}
