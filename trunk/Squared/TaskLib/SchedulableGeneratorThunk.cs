﻿using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
    public class SchedulableGeneratorThunk : ISchedulable, IDisposable {
        public Func<object, Future> OnNextValue = null;

        IEnumerator<object> _Task;
        Future _Future;
        public Future WakeCondition;
        TaskScheduler _Scheduler;
        Action _Step, _QueueStep;
        OnComplete _QueueStepOnComplete;

        public override string ToString () {
            return String.Format("<Task {0} waiting on {1}>", _Task, WakeCondition);
        }

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
            _QueueStep = QueueStep;
            _QueueStepOnComplete = QueueStepOnComplete;
            _Step = Step;
        }

        public void Dispose () {

            if (WakeCondition != null) {
                WakeCondition.Dispose();
                WakeCondition = null;
            }

            if (_Task != null) {
                _Task.Dispose();
                _Task = null;
            }

            if (_Future != null) {
                _Future.Dispose();
                _Future = null;
            }
        }

        void OnDisposed (Future _) {
            System.Diagnostics.Debug.WriteLine(String.Format("Task {0}'s future disposed. Aborting.", _Task));
            Dispose();
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            IEnumerator<object> task = _Task;
            _Future = future;
            _Scheduler = scheduler;
            _Future.RegisterOnDispose(this.OnDisposed);
            QueueStep();
        }

        void QueueStepOnComplete (Future f, object r, Exception e) {
            this.WakeCondition = null;
            _Scheduler.QueueWorkItem(_Step);
        }

        void QueueStep () {
            _Scheduler.QueueWorkItem(_Step);
        }

        void ScheduleNextStepForSchedulable (ISchedulable value) {
            if (value is WaitForNextStep) {
                _Scheduler.AddStepListener(_QueueStep);
            } else if (value is Yield) {
                QueueStep();
            } else {
                Future temp = _Scheduler.Start(value);
                this.WakeCondition = temp;
                temp.RegisterOnComplete(_QueueStepOnComplete);
            }
        }

        void ScheduleNextStep (Object value) {
            if (value is ISchedulable) {
                ScheduleNextStepForSchedulable(value as ISchedulable);
            } else if (value is NextValue) {
                NextValue nv = (NextValue)value;
                Future f = null;

                if (OnNextValue != null)
                    f = OnNextValue(nv.Value);

                if (f != null) {
                    this.WakeCondition = f;
                    f.RegisterOnComplete(_QueueStepOnComplete);
                } else {
                    QueueStep();
                }
            } else if (value is Future) {
                Future f = (Future)value;
                this.WakeCondition = f;
                f.RegisterOnComplete(_QueueStepOnComplete);
            } else if (value is Result) {
                _Future.Complete(((Result)value).Value);
                Dispose();
            } else {
                if (value is IEnumerator<object>) {
                    ScheduleNextStepForSchedulable(new RunToCompletion(value as IEnumerator<object>, TaskExecutionPolicy.RunAsBackgroundTask));
                } else if (value == null) {
                    QueueStep();
                } else {
                    throw new TaskYieldedValueException(_Task);
                }
            }
        }

        void Step () {
            if (_Task == null)
                return;

            WakeCondition = null;

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    _Future.Complete(null);
                    Dispose();
                    return;
                }

                // Disposed during execution
                if (_Task == null)
                    return;

                object value = _Task.Current;
                ScheduleNextStep(value);
            } catch (Exception ex) {
                if (_Future != null)
                    _Future.Fail(ex);
                Dispose();
            }
        }
    }
}