import type { SVGProps } from "react";

export interface IconProps extends SVGProps<SVGSVGElement> {
  size?: number;
}

function iconProps({ size = 18, ...props }: IconProps) {
  return {
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentcolor",
    strokeWidth: 1.8,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
    focusable: false,
    ...props,
  };
}

export function IconLogo(props: IconProps) {
  return (
    <svg {...iconProps({ size: 18, ...props })} stroke="none" fill="currentcolor">
      <path d="M12 2.5 21 7.2v9.6L12 21.5 3 16.8V7.2L12 2.5Z" opacity="0.35" />
      <path d="M12 6.2 17.5 9.1v5.8L12 17.8 6.5 14.9V9.1L12 6.2Z" />
    </svg>
  );
}

export function IconProjects(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="M4 7.5A2.5 2.5 0 0 1 6.5 5H10l1.6 2H17.5A2.5 2.5 0 0 1 20 9.5v7A2.5 2.5 0 0 1 17.5 19h-11A2.5 2.5 0 0 1 4 16.5v-9Z" />
      <path d="M4 10h16" />
    </svg>
  );
}

export function IconRuns(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <circle cx="12" cy="12" r="8.5" />
      <path d="m10.4 9.2 4.4 2.8-4.4 2.8V9.2Z" />
    </svg>
  );
}

export function IconSettings(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="M4 7h9" />
      <path d="M17 7h3" />
      <path d="M4 17h4" />
      <path d="M12 17h8" />
      <circle cx="15" cy="7" r="2" />
      <circle cx="10" cy="17" r="2" />
    </svg>
  );
}

export function IconPlus(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  );
}

export function IconClose(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="m6 6 12 12M18 6 6 18" />
    </svg>
  );
}

export function IconAlert(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="M10.3 4.3 2.9 17a2 2 0 0 0 1.7 3h14.8a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z" />
      <path d="M12 9v4" />
      <path d="M12 16.5h.01" />
    </svg>
  );
}

export function IconInbox(props: IconProps) {
  return (
    <svg {...iconProps(props)}>
      <path d="M4 13.5V7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v6.5" />
      <path d="M4 13.5h4l1.5 2h5l1.5-2h4V17a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-3.5Z" />
    </svg>
  );
}
