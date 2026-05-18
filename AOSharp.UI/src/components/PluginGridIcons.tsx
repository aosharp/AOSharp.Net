const iconStyle = { flexShrink: 0, display: 'block' } as const;

type IconProps = { size?: number };

export function LoadoutDotIcon({ size = 10 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <circle cx="8" cy="8" r="4" fill="currentColor" />
    </svg>
  );
}

export function EmDashIcon({ size = 14 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M3 8h10"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function ArrowUpIcon({ size = 12 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M8 3v10M8 3L4.5 6.5M8 3l3.5 3.5"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function CheckIcon({ size = 14 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M3.5 8.5 6.5 11.5 12.5 4.5"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function CrossIcon({ size = 14 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M4.5 4.5 11.5 11.5M11.5 4.5 4.5 11.5"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function RefreshIcon({ size = 16 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M13 3v3.5H9.5M3 13V9.5H6.5M13 8a5 5 0 0 0-8.2-3.7L3 6.5M3 8a5 5 0 0 0 8.2 3.7L13 9.5"
        stroke="currentColor"
        strokeWidth="1.35"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function WarningIcon({ size = 16 }: IconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden
      style={iconStyle}
    >
      <path
        d="M8 2.5 14 13.5H2L8 2.5Z"
        stroke="currentColor"
        strokeWidth="1.25"
        strokeLinejoin="round"
      />
      <path d="M8 6.5v3.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <circle cx="8" cy="12" r="0.75" fill="currentColor" />
    </svg>
  );
}

export function EmptyCellIcon() {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--color-text-muted)',
      }}
      aria-hidden
    >
      <EmDashIcon size={12} />
    </span>
  );
}
