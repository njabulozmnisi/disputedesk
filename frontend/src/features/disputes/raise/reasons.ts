import { Reason } from '@/api/generated/model';

/** The copy deck's label and explanation for each reason (EXPERIENCE.md, Copy deck: Reasons). */
export const reasonCopy: Record<Reason, { label: string; explanation: string }> = {
  unrecognised: { label: 'Unrecognised', explanation: "I don't recognise this transaction." },
  duplicateCharge: {
    label: 'Duplicate charge',
    explanation: 'I was charged more than once for the same thing.',
  },
  wrongAmount: {
    label: 'Wrong amount',
    explanation: 'The amount is different from what I agreed to pay.',
  },
  notReceived: {
    label: 'Not received',
    explanation: 'I paid but never got the goods or service.',
  },
  cancelledSubscription: {
    label: 'Cancelled subscription',
    explanation: 'I cancelled, but was still charged.',
  },
  refundNotProcessed: {
    label: 'Refund not processed',
    explanation: "I was promised a refund that hasn't arrived.",
  },
};

export const reasons = Object.values(Reason);
