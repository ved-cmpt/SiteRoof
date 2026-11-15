import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';
import { GITHUB_URL, PublicFooterComponent, PublicHeaderComponent } from './public-chrome';

@Component({
  selector: 'sr-case-study',
  imports: [RouterLink, IconComponent, PublicHeaderComponent, PublicFooterComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './case-study.page.html',
  styleUrl: './case-study.page.scss',
})
export class CaseStudyPage {
  protected readonly github = GITHUB_URL;

  protected readonly toc = [
    { id: 'problem', label: 'Problem' },
    { id: 'users', label: 'Users' },
    { id: 'rules', label: 'Business rules' },
    { id: 'architecture', label: 'Architecture' },
    { id: 'data', label: 'Data model' },
    { id: 'integration', label: 'ERP integration' },
    { id: 'stack', label: 'Why this stack' },
    { id: 'security', label: 'Security' },
    { id: 'testing', label: 'Testing' },
    { id: 'deployment', label: 'Deployment' },
    { id: 'walkthrough', label: 'Demo walkthrough' },
    { id: 'limits', label: 'Known limitations' },
  ];
}
