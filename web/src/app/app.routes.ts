import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'SiteRoof — Keep site work, decisions, and progress in one place',
    loadComponent: () => import('./public/landing.page').then((m) => m.LandingPage),
  },
  {
    path: 'case-study',
    title: 'Case study — SiteRoof',
    loadComponent: () => import('./public/case-study.page').then((m) => m.CaseStudyPage),
  },
  {
    path: 'demo',
    title: 'Preparing your demo — SiteRoof',
    loadComponent: () => import('./public/demo-start.page').then((m) => m.DemoStartPage),
  },
  {
    path: 'signin',
    title: 'Sign in — SiteRoof',
    loadComponent: () => import('./public/signin.page').then((m) => m.SignInPage),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        title: 'Overview — SiteRoof',
        loadComponent: () => import('./features/overview.page').then((m) => m.OverviewPage),
      },
      {
        path: 'projects',
        title: 'Projects — SiteRoof',
        loadComponent: () => import('./features/projects.page').then((m) => m.ProjectsPage),
      },
      {
        path: 'projects/:id',
        title: 'Project — SiteRoof',
        loadComponent: () => import('./features/project-detail.page').then((m) => m.ProjectDetailPage),
      },
      {
        path: 'work-orders/:id',
        title: 'Work order — SiteRoof',
        loadComponent: () => import('./features/work-order.page').then((m) => m.WorkOrderPage),
      },
      {
        path: 'approvals',
        title: 'Approvals — SiteRoof',
        loadComponent: () => import('./features/approvals.page').then((m) => m.ApprovalsPage),
      },
      {
        path: 'activity',
        title: 'Activity — SiteRoof',
        loadComponent: () => import('./features/activity.page').then((m) => m.ActivityPage),
      },
      {
        path: 'admin',
        title: 'Admin — SiteRoof',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin.page').then((m) => m.AdminPage),
      },
      {
        path: 'forbidden',
        title: 'Access denied — SiteRoof',
        loadComponent: () => import('./public/status.page').then((m) => m.ForbiddenPage),
      },
      {
        path: '**',
        title: 'Not found — SiteRoof',
        loadComponent: () => import('./public/status.page').then((m) => m.NotFoundPage),
      },
    ],
  },
  {
    path: '**',
    title: 'Not found — SiteRoof',
    loadComponent: () => import('./public/status.page').then((m) => m.NotFoundPage),
  },
];
