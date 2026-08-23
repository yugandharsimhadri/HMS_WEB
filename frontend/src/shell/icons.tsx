/**
 * The rail's icons.
 *
 * Drawn here rather than pulled from a package: the set is small, it has to
 * inherit `currentColor` so the active and hover states come from the theme
 * tokens alone, and a clinic's browser should not fetch an icon font to show
 * fourteen glyphs.
 *
 * All are 18x18 on a 24 grid, 1.7 stroke, round caps — consistent weight
 * matters more than cleverness at this size.
 */

type P = { className?: string };

const box = {
  width: 18,
  height: 18,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.7,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

export const IconDashboard = (p: P) => (
  <svg {...box} {...p}>
    <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
    <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
    <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
    <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
  </svg>
);

export const IconQueue = (p: P) => (
  <svg {...box} {...p}>
    <path d="M4 6h16M4 12h16M4 18h9" />
    <circle cx="19" cy="18" r="2.2" />
  </svg>
);

export const IconPatients = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="9" cy="8" r="3.4" />
    <path d="M2.8 20a6.2 6.2 0 0 1 12.4 0" />
    <path d="M17 11.2a3 3 0 0 0 0-6" />
    <path d="M18.4 20a5.6 5.6 0 0 0-2.5-4.6" />
  </svg>
);

export const IconCalendar = (p: P) => (
  <svg {...box} {...p}>
    <rect x="3" y="5" width="18" height="16" rx="2.2" />
    <path d="M3 10h18M8 3v4M16 3v4" />
  </svg>
);

export const IconDiagnostics = (p: P) => (
  <svg {...box} {...p}>
    <path d="M4 19V9M9 19V5M14 19v-6M19 19v-9" />
    <path d="M3 21h18" />
  </svg>
);

export const IconPediatrics = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="12" cy="9" r="4.6" />
    <path d="M10.3 8.4h.01M13.7 8.4h.01" />
    <path d="M10.4 11a2.6 2.6 0 0 0 3.2 0" />
    <path d="M6.5 21a5.5 5.5 0 0 1 11 0" />
  </svg>
);

export const IconDentist = (p: P) => (
  <svg {...box} {...p}>
    <path d="M12 3c-2.6 0-3.4 1-5 1S4 3.4 4 6.5c0 3.4 1.2 5.3 1.8 8.2.5 2.5.6 6.3 2.3 6.3 1.6 0 1.4-4.3 2.5-6.2.5-.9 2.3-.9 2.8 0 1.1 1.9.9 6.2 2.5 6.2 1.7 0 1.8-3.8 2.3-6.3.6-2.9 1.8-4.8 1.8-8.2C20 3.4 18.6 4 17 4s-2.4-1-5-1Z" />
  </svg>
);

export const IconLab = (p: P) => (
  <svg {...box} {...p}>
    <path d="M9.5 3v6.2L4.6 17.4A2.4 2.4 0 0 0 6.7 21h10.6a2.4 2.4 0 0 0 2.1-3.6L14.5 9.2V3" />
    <path d="M8.2 3h7.6" />
    <path d="M7.4 14.6h9.2" />
  </svg>
);

export const IconPharmacy = (p: P) => (
  <svg {...box} {...p}>
    <rect x="2.6" y="7.4" width="18.8" height="13.2" rx="2.2" />
    <path d="M12 11.2v5.6M9.2 14h5.6" />
    <path d="M6.6 7.4 8.4 3.4h7.2l1.8 4" />
  </svg>
);

export const IconMedicines = (p: P) => (
  <svg {...box} {...p}>
    <rect x="2.8" y="8.6" width="18.4" height="6.8" rx="3.4" transform="rotate(-45 12 12)" />
    <path d="M9.2 9.2 14.8 14.8" />
  </svg>
);

export const IconInventory = (p: P) => (
  <svg {...box} {...p}>
    <path d="M3 8.4 12 3.6l9 4.8v7.2L12 20.4 3 15.6Z" />
    <path d="M3 8.4 12 13.2l9-4.8M12 13.2v7.2" />
  </svg>
);

export const IconReports = (p: P) => (
  <svg {...box} {...p}>
    <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Z" />
    <path d="M14 3v5h5M9 13h6M9 17h4" />
  </svg>
);

export const IconMasters = (p: P) => (
  <svg {...box} {...p}>
    <path d="M3.4 7.2h17.2M3.4 12h17.2M3.4 16.8h17.2" />
    <circle cx="8.4" cy="7.2" r="2" />
    <circle cx="15.6" cy="12" r="2" />
    <circle cx="8.4" cy="16.8" r="2" />
  </svg>
);

export const IconSettings = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="12" cy="12" r="3.1" />
    <path d="M19.2 14.4a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1v.3a2 2 0 1 1-4 0v-.2a1.6 1.6 0 0 0-2.8-1.1l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0-1.1-2.7H3a2 2 0 1 1 0-4h.2a1.6 1.6 0 0 0 1.1-2.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3h.1A1.6 1.6 0 0 0 10 3.9v-.3a2 2 0 1 1 4 0v.2a1.6 1.6 0 0 0 2.7 1.1l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8v.1a1.6 1.6 0 0 0 1.5 1h.3a2 2 0 1 1 0 4h-.2a1.6 1.6 0 0 0-1.5 1Z" />
  </svg>
);

export const IconKey = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="7.6" cy="15.6" r="3.6" />
    <path d="M10.4 13 20 3.4M17.2 6.2l2.4 2.4M14.6 8.8l2.4 2.4" />
  </svg>
);

export const IconSignOut = (p: P) => (
  <svg {...box} {...p}>
    <path d="M9.6 20.4H5.4a2 2 0 0 1-2-2V5.6a2 2 0 0 1 2-2h4.2" />
    <path d="M16 16.4 20.4 12 16 7.6M20.4 12H9.6" />
  </svg>
);

export const IconSun = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="12" cy="12" r="4.2" />
    <path d="M12 2.4v2.2M12 19.4v2.2M4.2 4.2l1.6 1.6M18.2 18.2l1.6 1.6M2.4 12h2.2M19.4 12h2.2M4.2 19.8l1.6-1.6M18.2 5.8l1.6-1.6" />
  </svg>
);

export const IconMoon = (p: P) => (
  <svg {...box} {...p}>
    <path d="M20.4 13.6A8.4 8.4 0 1 1 10.4 3.6a6.6 6.6 0 0 0 10 10Z" />
  </svg>
);

export const IconHelp = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M9.6 9.4a2.5 2.5 0 0 1 4.8.8c0 1.7-2.4 2.2-2.4 3.6" />
    <path d="M12 17.2h.01" />
  </svg>
);

export const IconDensity = (p: P) => (
  <svg {...box} {...p}>
    <path d="M4 5h16M4 9.6h16M4 14.4h16M4 19h16" />
  </svg>
);

export const IconSearch = (p: P) => (
  <svg {...box} {...p}>
    <circle cx="10.6" cy="10.6" r="6.6" />
    <path d="M15.6 15.6 21 21" />
  </svg>
);
