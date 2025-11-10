import { Routes } from "@angular/router";
import { MainPageComponent } from "./containers/main-page/main-page.component";
import { AdvisorPageComponent } from "./containers/advisor-page/advisor-page.component";
import { LandingPageComponent } from "./containers/landing-page/landing-page.component";

export const routes: Routes = [
  {
    path: "",
    component: LandingPageComponent,
  },
  {
    path: "translation",
    component: MainPageComponent,
  },
  {
    path: "advisor",
    component: AdvisorPageComponent,
  },
  {
    path: "home",
    redirectTo: "",
    pathMatch: "full",
  },
];
