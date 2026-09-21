#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <glpk.h>


#if defined(_WIN32) || defined(_WIN64)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif


// === Indexing helpers ===
int x_idx(int q, int f, int F) {
    return q * F + f + 1;
}

int y_idx(int f, int Q, int F) {
    return Q * F + f + 1;
}

// === Build problem model ===
glp_prob* build_problem(const int64_t *mem, const double *cost, const int *z, int64_t MEM, int N, int Q, int F) {
    int QF = Q * F;
    int total_vars = QF + F;
    int max_nnz = (F + QF + 2 * QF + QF);

    // Allocate GLPK arrays
    int *ia = malloc((1 + max_nnz) * sizeof(int));
    int *ja = malloc((1 + max_nnz) * sizeof(int));
    double *ar = malloc((1 + max_nnz) * sizeof(double));
    int counter = 1;
    int row = 1;

    glp_prob *lp = glp_create_prob();
    glp_set_prob_name(lp, "ORMorpher ILP");
    glp_set_obj_dir(lp, GLP_MIN);
    glp_add_cols(lp, total_vars);

    // Declare x_{q,f} decision vars
    for (int q = 0; q < Q; q++) {
        for (int f = 0; f < F; f++) {
            int idx = x_idx(q, f, F);
            glp_set_col_kind(lp, idx, GLP_BV);
            glp_set_obj_coef(lp, idx, z[q] * cost[q * F + f]);
        }
    }

    // Declare y_f selection vars
    for (int f = 0; f < F; f++) {
        int idx = y_idx(f, Q, F);
        glp_set_col_kind(lp, idx, GLP_BV);
        glp_set_obj_coef(lp, idx, 0.0);
    }

    // Constraint (1): sum y_f <= N
    glp_add_rows(lp, 1);
    glp_set_row_bnds(lp, row, GLP_UP, 0.0, N);
    for (int f = 0; f < F; f++) {
        ia[counter] = row;
        ja[counter] = y_idx(f, Q, F);
        ar[counter] = 1.0;
        counter++;
    }
    row++;

    // Constraint (2): sum x_{q,f} == 1 for all q
    glp_add_rows(lp, Q);
    for (int q = 0; q < Q; q++, row++) {
        glp_set_row_bnds(lp, row, GLP_FX, 1.0, 1.0);
        for (int f = 0; f < F; f++) {
            ia[counter] = row;
            ja[counter] = x_idx(q, f, F);
            ar[counter] = 1.0;
            counter++;
        }
    }

    // Constraint (3): x_{q,f} <= y_f for all q,f
    glp_add_rows(lp, QF);
    for (int q = 0; q < Q; q++) {
        for (int f = 0; f < F; f++, row++) {
            glp_set_row_bnds(lp, row, GLP_UP, 0.0, 0.0);
            ia[counter] = row;
            ja[counter] = x_idx(q, f, F);
            ar[counter] = 1.0;
            counter++;

            ia[counter] = row;
            ja[counter] = y_idx(f, Q, F);
            ar[counter] = -1.0;
            counter++;
        }
    }

    // Constraint (4): sum mem[q,f] * x_{q,f} <= MEM
    glp_add_rows(lp, 1);
    glp_set_row_bnds(lp, row, GLP_UP, 0.0, (double)MEM);
    for (int q = 0; q < Q; q++) {
        for (int f = 0; f < F; f++) {
            ia[counter] = row;
            ja[counter] = x_idx(q, f, F);
            ar[counter] = (double)mem[q * F + f];
            counter++;
        }
    }

    // Load constraint matrix
    glp_load_matrix(lp, counter - 1, ia, ja, ar);
    free(ia); free(ja); free(ar);
    return lp;
}

// === Solve problem and extract solution ===
int** solve_problem(glp_prob *lp, int Q, int F, int *objective, int *selected) {
    glp_iocp parm;
    glp_init_iocp(&parm);
    parm.presolve = GLP_ON;
    int result = glp_intopt(lp, &parm);

    if (result != 0) {
        printf("No feasible solution found.\n");
        return NULL;
    }

    *objective = (int)glp_mip_obj_val(lp);

    int **x = malloc(Q * sizeof(int*));
    for (int q = 0; q < Q; q++) {
        x[q] = malloc(F * sizeof(int));
        for (int f = 0; f < F; f++) {
            x[q][f] = (int)(glp_mip_col_val(lp, x_idx(q, f, F)) + 0.5);
        }
    }

    for (int f = 0; f < F; f++) {
        selected[f] = (int)(glp_mip_col_val(lp, y_idx(f, Q, F)) + 0.5);
    }

    return x;
}

// Entry point for C# (P/Invoke) or C.
//
// Arguments:
//   mem           : int64_t[MEM] memory requirements, flat array
//   cost          : double[Q*F] cost table, flat array
//   z             : int[Q] weights
//   MEM           : (int64_t) total available memory
//   N             : (int) number of frameworks to choose
//   Q             : (int) number of queries
//   F             : (int) number of frameworks
//   out_objective : int*    out: receives optimal value
//   out_selected  : int[F]  out: 1 if framework f is selected, 0 otherwise
//   out_assignment: int[Q]  out: for each query, which framework it is assigned to (0..F-1 or -1)
// Returns 0 on success; negative on failure.
EXPORT int ilp_solve(
    const int64_t *mem,
    const double *cost,
    const int *z,
    int64_t MEM, int N, int Q, int F,
    int *out_objective,
    int *out_selected,
    int *out_assignment
)
{
    if (!out_objective || !out_selected || !out_assignment)
        return -2;

    glp_prob *lp = build_problem(mem, cost, z, MEM, N, Q, F);
    if (!lp) return -3;

    int **x = solve_problem(lp, Q, F, out_objective, out_selected);
    if (!x) {
        glp_delete_prob(lp);
        return -1;
    }

    // Convert x[q][f] one-hot matrix to a single assignment per q
    for (int q = 0; q < Q; ++q) {
        int assigned = -1;
        for (int f = 0; f < F; ++f) {
            if (x[q][f]) { assigned = f; break; }
        }
        out_assignment[q] = assigned;
        free(x[q]);
    }
    free(x);

    glp_delete_prob(lp);
    return 0;
}
