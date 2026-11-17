import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Label, Logo } from '@respawn/ui';

/** An address the front has no screen for. */
@Component({
  selector: 'rs-not-found',
  imports: [RouterLink, TranslocoDirective, Button, Label, Logo],
  templateUrl: './not-found.html',
})
export class NotFound {}
