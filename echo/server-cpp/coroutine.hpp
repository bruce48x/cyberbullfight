#pragma once

#include <coroutine>
#include <queue>
#include <chrono>
#include <unordered_set>

namespace server {

// Forward declaration
class Scheduler;

// Awaitable for sleep operation
struct SleepAwaiter {
    std::chrono::steady_clock::time_point deadline;
    Scheduler* scheduler = nullptr;
    int* timer_id = nullptr;
    
    bool await_ready() const {
        return std::chrono::steady_clock::now() >= deadline;
    }
    void await_suspend(std::coroutine_handle<> h) {
        // Coroutine is suspended, scheduler will resume it when deadline is reached
        // The coroutine handle is stored in the Task, scheduler will manage it
    }
    void await_resume() {}
};

// Simple coroutine task type
struct Task {
    struct promise_type {
        Scheduler* scheduler = nullptr;
        
        Task get_return_object() {
            return Task{std::coroutine_handle<promise_type>::from_promise(*this)};
        }
        std::suspend_never initial_suspend() { return {}; }
        std::suspend_always final_suspend() noexcept { return {}; }
        void return_void() {}
        void unhandled_exception() {}
        
        SleepAwaiter await_transform(std::chrono::milliseconds duration) {
            SleepAwaiter awaiter;
            awaiter.deadline = std::chrono::steady_clock::now() + duration;
            awaiter.scheduler = scheduler;
            return awaiter;
        }
    };

    std::coroutine_handle<promise_type> handle;

    Task(std::coroutine_handle<promise_type> h) : handle(h) {}
    ~Task() {
        if (handle) {
            handle.destroy();
        }
    }

    // Non-copyable
    Task(const Task&) = delete;
    Task& operator=(const Task&) = delete;

    // Movable
    Task(Task&& other) noexcept : handle(other.handle) {
        other.handle = {};
    }
    Task& operator=(Task&& other) noexcept {
        if (this != &other) {
            if (handle) handle.destroy();
            handle = other.handle;
            other.handle = {};
        }
        return *this;
    }

    bool resume() {
        if (handle && !handle.done()) {
            handle.resume();
            return !handle.done();
        }
        return false;
    }

    bool done() const {
        return !handle || handle.done();
    }
};

// Coroutine scheduler
class Scheduler {
public:
    struct TimerTask {
        std::chrono::steady_clock::time_point deadline;
        Task task;
        int id;

        TimerTask(int id, std::chrono::steady_clock::time_point deadline, Task&& task)
            : id(id), deadline(deadline), task(std::move(task)) {}
    };

    struct TimerCompare {
        bool operator()(const TimerTask& a, const TimerTask& b) const {
            return a.deadline > b.deadline; // Min-heap
        }
    };

    Scheduler() : next_id_(1) {}

    int add_task(Task&& task) {
        int id = next_id_++;
        ready_queue_.push({id, std::move(task)});
        return id;
    }

    int add_timer_task(std::chrono::milliseconds delay, Task&& task) {
        int id = next_id_++;
        auto deadline = std::chrono::steady_clock::now() + delay;
        // Set scheduler pointer in promise for await_transform
        if (task.handle) {
            task.handle.promise().scheduler = this;
        }
        timer_queue_.push({id, deadline, std::move(task)});
        return id;
    }

    void remove_timer(int id) {
        removed_timers_.insert(id);
    }

    void tick() {
        // Process ready tasks (one per tick to avoid starvation)
        if (!ready_queue_.empty()) {
            auto item = std::move(ready_queue_.front());
            ready_queue_.pop();
            if (!item.task.done()) {
                item.task.resume();
                if (!item.task.done()) {
                    // Task yielded, keep it for next tick
                    ready_queue_.push({item.id, std::move(item.task)});
                }
            }
        }

        // Process timer tasks
        auto now = std::chrono::steady_clock::now();
        while (!timer_queue_.empty()) {
            auto timer = std::move(const_cast<TimerTask&>(timer_queue_.top()));
            timer_queue_.pop();
            
            if (removed_timers_.count(timer.id)) {
                removed_timers_.erase(timer.id);
                continue;
            }
            
            if (timer.deadline <= now) {
                if (!timer.task.done()) {
                    timer.task.resume();
                    if (!timer.task.done()) {
                        // Task yielded (co_await), check if it's waiting for a deadline
                        // For now, reschedule immediately - the await_ready() will handle timing
                        // In practice, we should check the coroutine's suspension point
                        // For simplicity, reschedule with a small delay to avoid busy loop
                        timer_queue_.push({timer.id, now + std::chrono::milliseconds(10), std::move(timer.task)});
                    }
                }
            } else {
                // Not ready yet, put it back
                timer_queue_.push(std::move(timer));
                break;
            }
        }
    }

    std::chrono::milliseconds next_timeout() const {
        if (timer_queue_.empty()) {
            return std::chrono::milliseconds(1000); // Default 1 second
        }
        auto now = std::chrono::steady_clock::now();
        auto& timer = timer_queue_.top();
        if (timer.deadline <= now) {
            return std::chrono::milliseconds(0);
        }
        auto diff = timer.deadline - now;
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(diff);
        return ms.count() > 0 ? ms : std::chrono::milliseconds(1);
    }

    bool has_work() const {
        return !ready_queue_.empty() || 
               (!timer_queue_.empty() && 
                timer_queue_.top().deadline <= std::chrono::steady_clock::now());
    }

private:
    struct ReadyTask {
        int id;
        Task task;
    };

    std::queue<ReadyTask> ready_queue_;
    std::priority_queue<TimerTask, std::vector<TimerTask>, TimerCompare> timer_queue_;
    std::unordered_set<int> removed_timers_;
    int next_id_;
};

} // namespace server

